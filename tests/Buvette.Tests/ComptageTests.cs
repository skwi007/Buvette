using Buvette.Data;

namespace Buvette.Tests;

public class ComptageTests : TestAvecBase
{
    /// <summary>Une journée simple : 430,50 € de ventes sur un fond de caisse de 50 €.</summary>
    private async Task<CarteDeTest> JourneeAsync()
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse: 50m);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(
            ("Tarte flambée nature", 22),
            ("Tarte flambée gratinée", 19),
            ("Jus de pomme", 25),
            ("Bouteille d'eau", 16),
            ("Bretzel", 30)));
        return carte;
    }

    [Fact]
    public async Task Tant_que_personne_n_a_compte_il_n_y_a_pas_d_ecart()
    {
        var carte = await JourneeAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.False(recap!.Compte);
        Assert.Null(recap.Ecart);
        Assert.False(recap.EcartSignificatif);
    }

    [Fact]
    public async Task Une_caisse_juste_ne_signale_aucun_ecart()
    {
        var carte = await JourneeAsync();

        await Service.EnregistrerComptageAsync(carte.EvenementId, 480.50m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.True(recap!.Compte);
        Assert.Equal(0m, recap.Ecart);
        Assert.False(recap.EcartSignificatif);
    }

    [Fact]
    public async Task Un_manque_ressort_en_ecart_negatif()
    {
        var carte = await JourneeAsync();

        await Service.EnregistrerComptageAsync(carte.EvenementId, 460.50m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(-20m, recap!.Ecart);
        Assert.True(recap.EcartSignificatif);
    }

    [Fact]
    public async Task Un_excedent_ressort_en_ecart_positif()
    {
        var carte = await JourneeAsync();

        await Service.EnregistrerComptageAsync(carte.EvenementId, 485m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(4.50m, recap!.Ecart);
        Assert.True(recap.EcartSignificatif);
    }

    [Fact]
    public async Task Un_ecart_inferieur_au_centime_n_est_pas_signale()
    {
        var carte = await JourneeAsync();

        await Service.EnregistrerComptageAsync(carte.EvenementId, 480.505m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.True(recap!.Compte);
        Assert.False(recap.EcartSignificatif);
    }

    [Fact]
    public async Task Le_comptage_peut_etre_efface_pour_etre_refait()
    {
        var carte = await JourneeAsync();
        await Service.EnregistrerComptageAsync(carte.EvenementId, 400m);

        await Service.EnregistrerComptageAsync(carte.EvenementId, null);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.False(recap!.Compte);
        Assert.Null(recap.Ecart);
    }

    [Fact]
    public async Task Le_comptage_est_conserve_en_base()
    {
        var carte = await JourneeAsync();

        await Service.EnregistrerComptageAsync(carte.EvenementId, 480.50m);

        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);
        Assert.Equal(480.50m, evenement!.MontantCompte);
    }

    [Fact]
    public async Task Les_gratuites_expliquent_un_ecart_apparent()
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse: 50m);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 10))); // 80 €
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 3)), offerte: true);

        // Le tiroir contient le fond plus les seules ventes encaissées : la caisse tombe juste,
        // alors même que 13 tartes sont sorties du stock.
        await Service.EnregistrerComptageAsync(carte.EvenementId, 130m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(0m, recap!.Ecart);
        Assert.Equal(24m, recap.TotalOffert);
        Assert.Equal(13, recap.Produits.Single().QuantiteTotale);
    }

    [Fact]
    public async Task Annuler_une_commande_apres_comptage_fait_apparaitre_l_ecart()
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse: 0m);
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 10)));
        await Service.EnregistrerComptageAsync(carte.EvenementId, 10m);
        Assert.Equal(0m, (await Service.RecapitulatifAsync(carte.EvenementId))!.Ecart);

        await Service.AnnulerCommandeAsync(commande.Id);

        // Le comptage ne bouge pas : l'écart qui apparaît signale bien l'incohérence.
        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(10m, recap!.Ecart);
    }

    [Fact]
    public async Task Le_comptage_d_un_evenement_n_affecte_pas_les_autres()
    {
        var premiere = await JourneeAsync();
        var seconde = await JourneeAsync();

        await Service.EnregistrerComptageAsync(premiere.EvenementId, 400m);

        Assert.True((await Service.RecapitulatifAsync(premiere.EvenementId))!.Compte);
        Assert.False((await Service.RecapitulatifAsync(seconde.EvenementId))!.Compte);
    }

    [Fact]
    public async Task Un_ecran_de_reglages_reste_ouvert_n_ecrase_pas_un_comptage_saisi_entre_temps()
    {
        var carte = await JourneeAsync();

        // Quelqu'un ouvre la page Réglages : l'événement est chargé sans comptage.
        var reglagesOuverts = await Service.ChargerEvenementAsync(carte.EvenementId);
        Assert.Null(reglagesOuverts!.MontantCompte);

        // Pendant ce temps, le trésorier compte la caisse depuis la page Historique.
        await Service.EnregistrerComptageAsync(carte.EvenementId, 480.50m);

        // Puis le premier écran enregistre son formulaire, avec sa vision périmée.
        reglagesOuverts.Nom = "Fête de l'école 2026";
        await Service.EnregistrerEvenementAsync(reglagesOuverts);

        var relu = await Service.ChargerEvenementAsync(carte.EvenementId);
        Assert.Equal("Fête de l'école 2026", relu!.Nom);
        Assert.Equal(480.50m, relu.MontantCompte);
    }
}
