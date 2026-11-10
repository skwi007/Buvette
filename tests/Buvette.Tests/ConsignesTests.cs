using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class ConsignesTests : TestAvecBase
{
    /// <summary>Bière à 2 € et vin à 3 € servis dans des gobelets consignés 1 €, bretzels sans consigne.</summary>
    private async Task<CarteDeTest> BuvetteAvecConsigneAsync(decimal fondDeCaisse = 50m)
    {
        var id = await Service.CreerEvenementAsync(new Evenement
        {
            Nom = "Tournoi de foot",
            Date = new DateOnly(2026, 6, 13),
            FondDeCaisse = fondDeCaisse,
            Consigne = 1m,
        });
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "Bière", Prix = 2m, Consigne = true });
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "Vin", Prix = 3m, Consigne = true });
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "Bretzel", Prix = 1m });

        var produits = await Service.ProduitsActifsAsync(id);
        return new CarteDeTest(id, produits.ToDictionary(p => p.Nom, p => p.Id));
    }

    private static Dictionary<decimal, int> Verres(params (decimal Montant, int Nombre)[] verres) =>
        verres.ToDictionary(v => v.Montant, v => v.Nombre);

    [Fact]
    public async Task La_consigne_s_ajoute_au_prix_sans_entrer_dans_la_recette()
    {
        var carte = await BuvetteAvecConsigneAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));

        Assert.Equal(2m, commande.Total);
        Assert.Equal(1m, commande.Consignes);
        Assert.Equal(3m, commande.MontantEncaisse);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(2m, recap!.TotalVentes);
        Assert.Equal(1m, recap.Consignes!.Solde);
        Assert.Equal(53m, recap.TotalEnCaisse);
    }

    [Fact]
    public async Task Rendre_le_verre_rembourse_la_consigne()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));

        var retour = await Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>(),
            verresRendus: Verres((1m, 1)));

        Assert.Empty(retour.Lignes);
        Assert.Equal(-1m, retour.MontantEncaisse);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(0m, recap!.Consignes!.Solde);
        Assert.Equal(0, recap.Consignes.VerresEnCirculation);
        Assert.Equal(52m, recap.TotalEnCaisse);
    }

    [Fact]
    public async Task Un_client_peut_rendre_son_verre_et_en_reprendre_un_dans_le_meme_passage()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 2)));

        // Deux gobelets rendus, deux bières reprises : il ne paie que les bières.
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 2)),
            verresRendus: Verres((1m, 2)));

        Assert.Equal(4m, commande.MontantEncaisse);
        Assert.Equal(2, (await Service.VerresEnCirculationAsync(carte.EvenementId))[1m]);
    }

    [Fact]
    public async Task On_ne_rembourse_pas_plus_de_verres_qu_il_n_en_est_sorti()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>(), verresRendus: Verres((1m, 2))));

        await using var db = Base.Contexte();
        Assert.Equal(0, await db.RetoursConsignes.CountAsync());
    }

    [Fact]
    public async Task Les_verres_d_une_meme_commande_ne_peuvent_pas_etre_rendus_avec_elle()
    {
        var carte = await BuvetteAvecConsigneAsync();

        // Rien n'est encore dehors : la bière achetée maintenant n'a pas encore de verre à rendre.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)), verresRendus: Verres((1m, 1))));
    }

    [Fact]
    public async Task Seuls_les_produits_consignes_paient_la_consigne_de_l_evenement()
    {
        var carte = await BuvetteAvecConsigneAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId,
            carte.Panier(("Bière", 1), ("Vin", 2), ("Bretzel", 3)));

        Assert.Equal(3m, commande.Consignes);
        Assert.Equal(11m, commande.Total);
        Assert.Equal(3, (await Service.VerresEnCirculationAsync(carte.EvenementId))[1m]);
    }

    [Fact]
    public async Task Sans_montant_fixe_un_produit_consigne_se_vend_sans_consigne()
    {
        var id = await Service.CreerEvenementAsync(new Evenement { Nom = "Sans consigne", Date = new DateOnly(2026, 6, 13) });
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "Bière", Prix = 2m, Consigne = true });
        var biere = (await Service.ProduitsEnVenteAsync(id)).Single();

        var commande = await Service.EncaisserAsync(id, new Dictionary<int, int> { [biere.Id] = 1 });

        Assert.Null(biere.Consigne);
        Assert.Equal(2m, commande.MontantEncaisse);
    }

    [Fact]
    public async Task La_consigne_reste_due_sur_une_commande_offerte()
    {
        var carte = await BuvetteAvecConsigneAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)), offerte: true);

        Assert.Equal(1m, commande.MontantEncaisse);
        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(0m, recap!.TotalVentes);
        Assert.Equal(2m, recap.TotalOffert);
        Assert.Equal(51m, recap.TotalEnCaisse);
    }

    [Fact]
    public async Task La_consigne_est_figee_au_moment_de_la_vente()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));

        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);
        evenement!.Consigne = 1.5m;
        await Service.EnregistrerEvenementAsync(evenement);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));

        // Le gobelet vendu à 1 € se rembourse 1 €, même si la consigne a changé depuis.
        var enCirculation = await Service.VerresEnCirculationAsync(carte.EvenementId);
        Assert.Equal(1, enCirculation[1m]);
        Assert.Equal(1, enCirculation[1.5m]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>(), verresRendus: Verres((1.5m, 2))));
    }

    [Fact]
    public async Task Un_passage_qui_ne_fait_que_rendre_des_verres_n_est_pas_une_vente()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));
        await Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>(), verresRendus: Verres((1m, 1)));

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(1, recap!.NombreCommandes);
        Assert.Equal(2m, recap.PanierMoyen);
    }

    [Fact]
    public async Task Annuler_un_retour_de_verres_remet_la_consigne_en_circulation()
    {
        var carte = await BuvetteAvecConsigneAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bière", 1)));
        var retour = await Service.EncaisserAsync(carte.EvenementId, new Dictionary<int, int>(),
            verresRendus: Verres((1m, 1)));

        await Service.AnnulerCommandeAsync(retour.Id);

        Assert.Equal(1, (await Service.VerresEnCirculationAsync(carte.EvenementId))[1m]);
        await using var db = Base.Contexte();
        Assert.Equal(0, await db.RetoursConsignes.CountAsync());
    }

    [Fact]
    public async Task La_consigne_est_reconduite_quand_on_copie_la_carte()
    {
        var carte = await BuvetteAvecConsigneAsync();

        var copie = await Service.CreerEvenementAsync(
            new Evenement { Nom = "Tournoi d'automne", Date = new DateOnly(2026, 10, 3) }, carte.EvenementId);

        var produits = await Service.ProduitsEnVenteAsync(copie);
        Assert.Equal(1m, (await Service.ChargerEvenementAsync(copie))!.Consigne);
        Assert.Equal(1m, produits.Single(p => p.Nom == "Bière").Consigne);
        Assert.Null(produits.Single(p => p.Nom == "Bretzel").Consigne);
    }

    [Fact]
    public async Task Sans_produit_consigne_le_recapitulatif_ne_parle_pas_de_consigne()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Null(recap!.Consignes);
    }
}
