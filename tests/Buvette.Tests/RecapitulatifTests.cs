using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class RecapitulatifTests : TestAvecBase
{
    /// <summary>Rejoue la journée de l'énoncé : 22 / 19 / 25 / 16 / 30 articles vendus.</summary>
    private async Task<CarteDeTest> JourneeCompleteAsync(decimal fondDeCaisse = 50m)
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse);
        var journee = new (string Nom, int Quantite)[]
        {
            ("Tarte flambée nature", 22),
            ("Tarte flambée gratinée", 19),
            ("Jus de pomme", 25),
            ("Bouteille d'eau", 16),
            ("Bretzel", 30),
        };

        // Vendus un par un, comme au comptoir, pour vérifier l'agrégation sur de vraies commandes.
        foreach (var (nom, quantite) in journee)
            for (var i = 0; i < quantite; i++)
                await Service.EncaisserAsync(carte.EvenementId, carte.Panier((nom, 1)));

        return carte;
    }

    [Fact]
    public async Task Le_recapitulatif_totalise_les_quantites_vendues_par_produit()
    {
        var carte = await JourneeCompleteAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(22, recap!.Produits.Single(p => p.Nom == "Tarte flambée nature").Quantite);
        Assert.Equal(19, recap.Produits.Single(p => p.Nom == "Tarte flambée gratinée").Quantite);
        Assert.Equal(25, recap.Produits.Single(p => p.Nom == "Jus de pomme").Quantite);
        Assert.Equal(16, recap.Produits.Single(p => p.Nom == "Bouteille d'eau").Quantite);
        Assert.Equal(30, recap.Produits.Single(p => p.Nom == "Bretzel").Quantite);
    }

    [Fact]
    public async Task Le_recapitulatif_donne_le_montant_par_produit()
    {
        var carte = await JourneeCompleteAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(176m, recap!.Produits.Single(p => p.Nom == "Tarte flambée nature").Montant);  // 22 × 8
        Assert.Equal(171m, recap.Produits.Single(p => p.Nom == "Tarte flambée gratinée").Montant); // 19 × 9
        Assert.Equal(37.5m, recap.Produits.Single(p => p.Nom == "Jus de pomme").Montant);          // 25 × 1,50
        Assert.Equal(16m, recap.Produits.Single(p => p.Nom == "Bouteille d'eau").Montant);         // 16 × 1
        Assert.Equal(30m, recap.Produits.Single(p => p.Nom == "Bretzel").Montant);                 // 30 × 1
    }

    [Fact]
    public async Task Le_total_des_ventes_est_la_somme_des_montants()
    {
        var carte = await JourneeCompleteAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(430.5m, recap!.TotalVentes);
    }

    [Fact]
    public async Task Le_montant_attendu_en_caisse_ajoute_le_fond_de_caisse()
    {
        var carte = await JourneeCompleteAsync(fondDeCaisse: 50m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(480.5m, recap!.TotalEnCaisse);
    }

    [Fact]
    public async Task Sans_fond_de_caisse_le_montant_attendu_egale_les_ventes()
    {
        var carte = await JourneeCompleteAsync(fondDeCaisse: 0m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(recap!.TotalVentes, recap.TotalEnCaisse);
    }

    [Fact]
    public async Task Le_recapitulatif_compte_les_commandes_et_calcule_le_panier_moyen()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 1))); // 8
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Jus de pomme", 2)));         // 3
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));              // 1

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(3, recap!.NombreCommandes);
        Assert.Equal(12m, recap.TotalVentes);
        Assert.Equal(4m, recap.PanierMoyen);
    }

    [Fact]
    public async Task Le_panier_moyen_vaut_zero_sans_aucune_commande()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(0, recap!.NombreCommandes);
        Assert.Equal(0m, recap.PanierMoyen);
    }

    [Fact]
    public async Task Une_buvette_sans_vente_affiche_un_recapitulatif_vide_mais_son_fond_de_caisse()
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse: 50m);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Empty(recap!.Produits);
        Assert.Equal(0m, recap.TotalVentes);
        Assert.Equal(50m, recap.TotalEnCaisse);
    }

    [Fact]
    public async Task Le_recapitulatif_classe_les_produits_du_plus_gros_montant_au_plus_petit()
    {
        var carte = await JourneeCompleteAsync();

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(
            ["Tarte flambée nature", "Tarte flambée gratinée", "Jus de pomme", "Bretzel", "Bouteille d'eau"],
            recap!.Produits.Select(p => p.Nom));
    }

    [Fact]
    public async Task Un_produit_vendu_a_deux_prix_apparait_sur_deux_lignes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 10)));

        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");
        bretzel.Prix = 1.2m;
        await Service.EnregistrerProduitAsync(bretzel);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 5)));

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        var lignes = recap!.Produits.Where(p => p.Nom == "Bretzel").OrderBy(p => p.PrixUnitaire).ToList();

        Assert.Equal(2, lignes.Count);
        Assert.Equal((1m, 10, 10m), (lignes[0].PrixUnitaire, lignes[0].Quantite, lignes[0].Montant));
        Assert.Equal((1.2m, 5, 6m), (lignes[1].PrixUnitaire, lignes[1].Quantite, lignes[1].Montant));
        Assert.Equal(16m, recap.TotalVentes);
    }

    [Fact]
    public async Task Le_recapitulatif_ignore_les_ventes_des_autres_evenements()
    {
        var buvette = await Base.FeteDeLEcoleAsync();
        var voisine = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(buvette.EvenementId, buvette.Panier(("Bretzel", 3)));
        await Service.EncaisserAsync(voisine.EvenementId, voisine.Panier(("Bretzel", 100)));

        var recap = await Service.RecapitulatifAsync(buvette.EvenementId);

        Assert.Equal(3m, recap!.TotalVentes);
        Assert.Equal(1, recap.NombreCommandes);
    }

    [Fact]
    public async Task Le_recapitulatif_d_un_evenement_inconnu_renvoie_null()
    {
        Assert.Null(await Service.RecapitulatifAsync(404));
    }

    [Fact]
    public async Task Annuler_une_commande_la_retire_des_totaux()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var gardee = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 1)));
        var erronee = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 30)));

        await Service.AnnulerCommandeAsync(erronee.Id);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(8m, recap!.TotalVentes);
        Assert.Equal(1, recap.NombreCommandes);
        Assert.DoesNotContain(recap.Produits, p => p.Nom == "Bretzel");
        Assert.Equal(gardee.Id, (await Service.ListerCommandesAsync(carte.EvenementId)).Single().Id);
    }

    [Fact]
    public async Task Annuler_une_commande_efface_aussi_ses_lignes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3), ("Jus de pomme", 1)));

        await Service.AnnulerCommandeAsync(commande.Id);

        await using var db = Base.Contexte();
        Assert.Equal(0, await db.Lignes.CountAsync());
    }

    [Fact]
    public async Task Les_commandes_sont_listees_de_la_plus_recente_a_la_plus_ancienne()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var premiere = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));
        var seconde = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));

        var commandes = await Service.ListerCommandesAsync(carte.EvenementId);

        Assert.Equal([seconde.Id, premiere.Id], commandes.Select(c => c.Id));
    }

    [Fact]
    public async Task Les_commandes_listees_portent_le_detail_de_leurs_lignes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3), ("Jus de pomme", 1)));

        var commande = Assert.Single(await Service.ListerCommandesAsync(carte.EvenementId));

        Assert.Equal(2, commande.Lignes.Count);
        Assert.Contains(commande.Lignes, l => l.NomProduit == "Bretzel" && l.Quantite == 3);
        Assert.Equal(4.5m, commande.Total);
    }
}
