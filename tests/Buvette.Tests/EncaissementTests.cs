using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class EncaissementTests : TestAvecBase
{
    [Fact]
    public async Task La_commande_de_l_enonce_totalise_23_euros_50()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        // 1 nature (8) + 1 gratinée (9) + 1 jus (1,50) + 2 eaux (2) + 3 bretzels (3)
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(
            ("Tarte flambée nature", 1),
            ("Tarte flambée gratinée", 1),
            ("Jus de pomme", 1),
            ("Bouteille d'eau", 2),
            ("Bretzel", 3)));

        Assert.Equal(23.5m, commande.Total);
        Assert.Equal(5, commande.Lignes.Count);
    }

    [Fact]
    public async Task Chaque_ligne_recopie_le_nom_et_le_prix_pratiques()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Jus de pomme", 2)));

        var ligne = Assert.Single(commande.Lignes);
        Assert.Equal("Jus de pomme", ligne.NomProduit);
        Assert.Equal(1.5m, ligne.PrixUnitaire);
        Assert.Equal(2, ligne.Quantite);
        Assert.Equal(3m, ligne.SousTotal);
    }

    [Fact]
    public async Task Le_total_est_recalcule_depuis_les_prix_en_base()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");

        // Un écran de caisse resté ouvert affiche encore 1,00 € ; la base fait foi.
        bretzel.Prix = 1.5m;
        await Service.EnregistrerProduitAsync(bretzel);

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));

        Assert.Equal(3m, commande.Total);
        Assert.Equal(1.5m, commande.Lignes.Single().PrixUnitaire);
    }

    [Fact]
    public async Task Une_vente_deja_encaissee_ne_bouge_plus_si_le_prix_change_ensuite()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 10)));

        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");
        bretzel.Prix = 5m;
        await Service.EnregistrerProduitAsync(bretzel);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(10m, recap!.TotalVentes);
    }

    [Fact]
    public async Task Un_panier_vide_est_refuse()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>()));

        Assert.Contains("vide", erreur.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_panier_dont_toutes_les_quantites_sont_nulles_est_refuse()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 0), ("Jus de pomme", 0))));
    }

    [Fact]
    public async Task Les_quantites_nulles_sont_ignorees_sans_fausser_le_total()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId,
            carte.Panier(("Bretzel", 2), ("Jus de pomme", 0)));

        Assert.Equal(2m, commande.Total);
        Assert.Single(commande.Lignes);
    }

    [Fact]
    public async Task Une_buvette_cloturee_refuse_toute_nouvelle_vente()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);
        evenement!.Cloture = true;
        await Service.EnregistrerEvenementAsync(evenement);

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1))));

        Assert.Contains("clôturé", erreur.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reouvrir_une_buvette_cloturee_reautorise_les_ventes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);
        evenement!.Cloture = true;
        await Service.EnregistrerEvenementAsync(evenement);
        evenement.Cloture = false;
        await Service.EnregistrerEvenementAsync(evenement);

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        Assert.Equal(1m, commande.Total);
    }

    [Fact]
    public async Task Encaisser_sur_un_evenement_inconnu_est_refuse()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(404, carte.Panier(("Bretzel", 1))));
    }

    [Fact]
    public async Task Un_produit_supprime_entre_temps_bloque_l_encaissement()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var panier = carte.Panier(("Bretzel", 2), ("Jus de pomme", 1));
        await Service.SupprimerProduitAsync(carte["Jus de pomme"]);

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, panier));

        Assert.Contains("supprimé", erreur.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_encaissement_refuse_n_ecrit_rien_en_base()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var panier = carte.Panier(("Bretzel", 2), ("Jus de pomme", 1));
        await Service.SupprimerProduitAsync(carte["Jus de pomme"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, panier));

        await using var db = Base.Contexte();
        Assert.Equal(0, await db.Commandes.CountAsync());
        Assert.Equal(0, await db.Lignes.CountAsync());
    }

    [Fact]
    public async Task Un_produit_appartenant_a_un_autre_evenement_est_refuse()
    {
        var buvette = await Base.FeteDeLEcoleAsync();
        var voisine = await Base.FeteDeLEcoleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(buvette.EvenementId, new Dictionary<int, int> { [voisine["Bretzel"]] = 1 }));
    }

    [Fact]
    public async Task Les_montants_restent_exacts_au_centime_sans_derive()
    {
        var id = await Service.CreerEvenementAsync(new Evenement { Nom = "Centimes", Date = new DateOnly(2026, 9, 26) });
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "Café", Prix = 1.10m });
        var cafe = (await Service.ProduitsActifsAsync(id)).Single();

        // 0,10 n'est pas représentable en binaire : un total calculé en double dériverait.
        var commande = await Service.EncaisserAsync(id, new Dictionary<int, int> { [cafe.Id] = 3 });

        Assert.Equal(3.30m, commande.Total);
        var recap = await Service.RecapitulatifAsync(id);
        Assert.Equal(3.30m, recap!.TotalVentes);
    }

    [Fact]
    public async Task Chaque_passage_en_caisse_cree_une_commande_distincte()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        var commandes = await Service.ListerCommandesAsync(carte.EvenementId);
        Assert.Equal(2, commandes.Count);
        Assert.Equal(2, commandes.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public async Task Lister_les_commandes_ne_renvoie_que_celles_de_l_evenement_demande()
    {
        var buvette = await Base.FeteDeLEcoleAsync();
        var voisine = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(buvette.EvenementId, buvette.Panier(("Bretzel", 1)));
        await Service.EncaisserAsync(voisine.EvenementId, voisine.Panier(("Bretzel", 5)));

        var commandes = await Service.ListerCommandesAsync(buvette.EvenementId);

        Assert.Equal(1m, Assert.Single(commandes).Total);
    }
}
