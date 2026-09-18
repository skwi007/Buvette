using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class StockTests : TestAvecBase
{
    [Fact]
    public async Task Un_produit_sans_stock_saisi_reste_illimite()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var bretzel = (await Service.ProduitsEnVenteAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");

        Assert.Null(bretzel.StockRestant);
        Assert.False(bretzel.StockSuivi);
        Assert.False(bretzel.Epuise);
    }

    [Fact]
    public async Task Un_produit_illimite_se_vend_sans_limite()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 500)));

        Assert.Null(await Base.RestantAsync(carte, "Bretzel"));
    }

    [Fact]
    public async Task Le_stock_restant_diminue_a_chaque_vente()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Tarte flambée nature", 30);

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 2)));
        Assert.Equal(28, await Base.RestantAsync(carte, "Tarte flambée nature"));

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Tarte flambée nature", 3)));
        Assert.Equal(25, await Base.RestantAsync(carte, "Tarte flambée nature"));
    }

    [Fact]
    public async Task Vendre_exactement_le_stock_est_accepte_et_epuise_le_produit()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 3);

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3)));

        var bretzel = (await Service.ProduitsEnVenteAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");
        Assert.Equal(0, bretzel.StockRestant);
        Assert.True(bretzel.Epuise);
    }

    [Fact]
    public async Task Vendre_plus_que_le_stock_est_refuse()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 3);

        var erreur = await Assert.ThrowsAsync<StockInsuffisantException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 4))));

        Assert.Equal("Bretzel", erreur.Produit);
        Assert.Equal(3, erreur.Restant);
        Assert.Contains("3", erreur.Message);
    }

    [Fact]
    public async Task Un_produit_epuise_ne_peut_plus_etre_vendu()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 2);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));

        var erreur = await Assert.ThrowsAsync<StockInsuffisantException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1))));

        Assert.Equal(0, erreur.Restant);
        Assert.Contains("épuisé", erreur.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Une_commande_refusee_pour_stock_n_encaisse_rien_du_tout()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 1);

        // Le bretzel manque, mais la tarte est disponible : la commande doit être
        // refusée en bloc, sans encaisser la partie servable.
        await Assert.ThrowsAsync<StockInsuffisantException>(
            () => Service.EncaisserAsync(carte.EvenementId,
                carte.Panier(("Tarte flambée nature", 1), ("Bretzel", 5))));

        await using var db = Base.Contexte();
        Assert.Equal(0, await db.Commandes.CountAsync());
        Assert.Equal(0, await db.Lignes.CountAsync());
        Assert.Equal(1, await Base.RestantAsync(carte, "Bretzel"));
    }

    [Fact]
    public async Task Annuler_une_commande_remet_les_articles_a_disposition()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 10);
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 7)));
        Assert.Equal(3, await Base.RestantAsync(carte, "Bretzel"));

        await Service.AnnulerCommandeAsync(commande.Id);

        Assert.Equal(10, await Base.RestantAsync(carte, "Bretzel"));
    }

    [Fact]
    public async Task Un_reapprovisionnement_en_cours_de_journee_remet_le_produit_en_vente()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 5);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 5)));
        Assert.Equal(0, await Base.RestantAsync(carte, "Bretzel"));

        await Base.DefinirStockAsync(carte, "Bretzel", 25);

        Assert.Equal(20, await Base.RestantAsync(carte, "Bretzel"));
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));
        Assert.Equal(2m, commande.Total);
    }

    [Fact]
    public async Task Baisser_le_stock_sous_le_deja_vendu_affiche_epuise_et_non_un_negatif()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 20);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 15)));

        // Le responsable se rend compte qu'il n'y en avait que 10 au départ.
        await Base.DefinirStockAsync(carte, "Bretzel", 10);

        Assert.Equal(0, await Base.RestantAsync(carte, "Bretzel"));
    }

    [Fact]
    public async Task Effacer_le_stock_rend_le_produit_a_nouveau_illimite()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 1);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        await Base.DefinirStockAsync(carte, "Bretzel", null);

        Assert.Null(await Base.RestantAsync(carte, "Bretzel"));
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 50)));
    }

    [Fact]
    public async Task Un_stock_a_zero_des_le_depart_bloque_toute_vente()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 0);

        Assert.Equal(0, await Base.RestantAsync(carte, "Bretzel"));
        await Assert.ThrowsAsync<StockInsuffisantException>(
            () => Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1))));
    }

    [Fact]
    public async Task Le_stock_d_un_evenement_ignore_les_ventes_d_un_autre()
    {
        var buvette = await Base.FeteDeLEcoleAsync();
        var voisine = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(buvette, "Bretzel", 10);
        await Base.DefinirStockAsync(voisine, "Bretzel", 10);

        await Service.EncaisserAsync(voisine.EvenementId, voisine.Panier(("Bretzel", 8)));

        Assert.Equal(10, await Base.RestantAsync(buvette, "Bretzel"));
        Assert.Equal(2, await Base.RestantAsync(voisine, "Bretzel"));
    }

    [Theory]
    [InlineData(0, false)]  // épuisé, signalé autrement
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task L_alerte_bientot_epuise_se_declenche_a_cinq_articles_ou_moins(int restant, bool attendu)
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", restant);

        var bretzel = (await Service.ProduitsEnVenteAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");

        Assert.Equal(attendu, bretzel.BientotEpuise);
    }

    [Fact]
    public async Task Un_produit_illimite_ne_declenche_jamais_d_alerte()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var bretzel = (await Service.ProduitsEnVenteAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");

        Assert.False(bretzel.BientotEpuise);
        Assert.False(bretzel.Epuise);
    }

    [Fact]
    public async Task Le_stock_prevu_se_reconduit_lors_de_la_reprise_d_une_carte()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 40);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 35)));

        var suivante = await Service.CreerEvenementAsync(
            new Evenement { Nom = "Fête 2027", Date = new DateOnly(2027, 9, 25) },
            copierProduitsDepuis: carte.EvenementId);

        var bretzel = (await Service.ProduitsEnVenteAsync(suivante)).Single(p => p.Nom == "Bretzel");
        // La quantité prévue est reprise, mais les ventes de l'an dernier ne le sont pas.
        Assert.Equal(40, bretzel.StockRestant);
    }

    [Fact]
    public async Task Les_stocks_restants_couvrent_aussi_les_produits_retires_de_la_vente()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 10);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 4)));

        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");
        bretzel.Actif = false;
        await Service.EnregistrerProduitAsync(bretzel);

        // La page Produits doit continuer d'afficher ce qu'il reste, même hors vente.
        var stocks = await Service.StocksRestantsAsync(carte.EvenementId);
        Assert.Equal(6, stocks[bretzel.Id]);
        Assert.DoesNotContain(await Service.ProduitsEnVenteAsync(carte.EvenementId), p => p.Nom == "Bretzel");
    }

    [Fact]
    public async Task Les_produits_illimites_n_apparaissent_pas_dans_les_stocks_restants()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 10);

        var stocks = await Service.StocksRestantsAsync(carte.EvenementId);

        Assert.Single(stocks);
        Assert.True(stocks.ContainsKey(carte["Bretzel"]));
    }

    [Fact]
    public async Task Le_stock_n_empeche_pas_le_recapitulatif_d_etre_juste()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 10);
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 10)));

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);

        Assert.Equal(10, recap!.Produits.Single(p => p.Nom == "Bretzel").Quantite);
        Assert.Equal(10m, recap.TotalVentes);
    }
}
