using Buvette.Data;

namespace Buvette.Tests;

public class GratuitesTests : TestAvecBase
{
    [Fact]
    public async Task Une_commande_offerte_n_entre_pas_dans_la_recette()
    {
        var carte = await Base.FeteDeLEcoleAsync(fondDeCaisse: 50m);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 1)));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 2)), offerte: true);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(8m, recap!.TotalVentes);
        Assert.Equal(16m, recap.TotalOffert);
        Assert.Equal(58m, recap.TotalEnCaisse);   // fond + ventes encaissées seulement
    }

    [Fact]
    public async Task Une_commande_offerte_consomme_quand_meme_le_stock()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 10);

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 4)), offerte: true);

        Assert.Equal(6, await Base.RestantAsync(carte, "Bretzel"));
    }

    [Fact]
    public async Task Un_produit_epuise_par_des_gratuites_ne_peut_plus_etre_vendu()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 3);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3)), offerte: true);

        await Assert.ThrowsAsync<StockInsuffisantException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1))));
    }

    [Fact]
    public async Task Le_recapitulatif_separe_le_vendu_de_l_offert_pour_un_meme_produit()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 20)));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 5)), offerte: true);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        var bretzel = recap!.Produits.Single(p => p.Nom == "Bretzel");

        Assert.Equal(20, bretzel.Quantite);
        Assert.Equal(20m, bretzel.Montant);
        Assert.Equal(5, bretzel.QuantiteOfferte);
        Assert.Equal(5m, bretzel.MontantOffert);
        Assert.Equal(25, bretzel.QuantiteTotale);
    }

    [Fact]
    public async Task Les_commandes_offertes_sont_comptees_a_part()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)), offerte: true);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(2, recap!.NombreCommandes);
        Assert.Equal(1, recap.NombreOffertes);
    }

    [Fact]
    public async Task Le_panier_moyen_ignore_les_gratuites()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 1))); // 8 €
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));              // 2 €
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 5)), offerte: true);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        // 10 € sur 2 commandes payantes : la gratuité ne doit pas écraser la moyenne.
        Assert.Equal(5m, recap!.PanierMoyen);
    }

    [Fact]
    public async Task Une_commande_offerte_garde_la_trace_de_sa_valeur()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var commande = await Service.EncaisserAsync(
            carte.EvenementId, carte.Panier(("Tarte flambée nature", 2)), offerte: true);

        Assert.True(commande.Offerte);
        Assert.Equal(16m, commande.Total);   // ce que ça aurait coûté
    }

    [Fact]
    public async Task Annuler_une_commande_offerte_la_retire_du_total_offert()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var offerte = await Service.EncaisserAsync(
            carte.EvenementId, carte.Panier(("Bretzel", 5)), offerte: true);

        await Service.AnnulerCommandeAsync(offerte.Id);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        Assert.Equal(0m, recap!.TotalOffert);
        Assert.Equal(0, recap.NombreOffertes);
    }

    [Fact]
    public async Task Sans_aucune_gratuite_les_totaux_offerts_sont_nuls()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3)));

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(0m, recap!.TotalOffert);
        Assert.Equal(0, recap.NombreOffertes);
        Assert.Equal(0, recap.Produits.Single().QuantiteOfferte);
    }

    [Fact]
    public async Task Une_commande_est_payante_par_defaut()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        Assert.False(commande.Offerte);
        Assert.Equal(1m, (await Service.RecapitulatifAsync(carte.EvenementId))!.TotalVentes);
    }

    [Fact]
    public async Task Une_buvette_cloturee_refuse_aussi_les_gratuites()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);
        evenement!.Cloture = true;
        await Service.EnregistrerEvenementAsync(evenement);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)), offerte: true));
    }
}
