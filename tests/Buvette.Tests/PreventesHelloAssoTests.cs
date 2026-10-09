using Buvette.Data;
using Buvette.HelloAsso;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buvette.Tests;

public class PreventesHelloAssoTests : TestAvecBase
{
    // Les tarifs de la vraie campagne Saint-Martin, espaces de fin compris.
    private static readonly List<TarifHelloAsso> Tarifs =
    [
        new(101, "Bretzel", 150, "Registration"),
        new(102, "Tarte flambée nature ", 900, "Registration"),
        new(103, "Tarte flambée gratinée", 1000, "Registration"),
        new(104, "Part de gâteau", 150, "Registration"),
    ];

    private readonly SynchroHelloAsso synchro;

    public PreventesHelloAssoTests()
    {
        // Le client n'est jamais appelé : les tests passent les données HelloAsso directement.
        var client = new HelloAssoClient(new SansReseau(), Options.Create(new HelloAssoOptions()), TimeProvider.System);
        synchro = new SynchroHelloAsso(client, Base.Fabrique, Base.Notifications);
    }

    private sealed class SansReseau : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Pas de réseau dans les tests.");
    }

    private async Task<int> SaintMartinAsync() =>
        await Service.CreerEvenementAsync(new Evenement
        {
            Nom = "Saint-Martin",
            Date = new DateOnly(2026, 11, 13),
            FondDeCaisse = 50m,
            HelloAssoType = "Event",
            HelloAssoCampagne = "saint-martin",
        });

    private static ArticleHelloAsso Article(long tarif, string nom, int centimes, string etat = "Processed", string type = "Registration") =>
        new(Random.Shared.NextInt64(), nom, centimes, etat, type, tarif, null, null);

    private static CommandeHelloAsso Commande(long numero, string nom, string prenom, params ArticleHelloAsso[] articles) =>
        new(numero, new DateTimeOffset(2026, 10, 7, 9, 31, 0, TimeSpan.FromHours(2)),
            new PersonneHelloAsso(prenom, nom, null), [.. articles],
            new MontantHelloAsso(articles.Sum(a => a.Amount)), [new PaiementHelloAsso(1, articles.Sum(a => a.Amount), "Authorized")]);

    private static CommandeHelloAsso Brey() => Commande(194845923, "BREY", "Anaïs",
        Article(101, "Bretzel", 150), Article(101, "Bretzel", 150), Article(101, "Bretzel", 150),
        Article(102, "Tarte flambée nature ", 900),
        Article(103, "Tarte flambée gratinée", 1000), Article(103, "Tarte flambée gratinée", 1000));

    private static CommandeHelloAsso Goepfert() => Commande(194863320, "GOEPFERT", "Marie",
        Article(101, "Bretzel", 150), Article(103, "Tarte flambée gratinée", 1000));

    private async Task<Produit> ProduitAsync(int evenementId, string nom)
    {
        await using var db = Base.Contexte();
        return await db.Produits.SingleAsync(p => p.EvenementId == evenementId && p.Nom == nom);
    }

    [Fact]
    public async Task Chaque_tarif_devient_un_produit_meme_sans_vente()
    {
        var id = await SaintMartinAsync();

        var resultat = await synchro.AppliquerAsync(id, Tarifs, [Brey()]);

        var produits = await Service.ProduitsActifsAsync(id);
        Assert.Equal(["Bretzel", "Tarte flambée nature", "Tarte flambée gratinée", "Part de gâteau"],
            produits.Select(p => p.Nom));
        Assert.Equal(9m, produits.Single(p => p.Nom == "Tarte flambée nature").Prix);
        Assert.Equal(4, resultat.ProduitsCrees);
        Assert.All(produits, p => Assert.NotNull(p.TarifHelloAssoId));
    }

    [Fact]
    public async Task Les_commandes_deviennent_des_precommandes_regroupees_par_article()
    {
        var id = await SaintMartinAsync();

        await synchro.AppliquerAsync(id, Tarifs, [Brey(), Goepfert()]);

        var brey = (await Service.ListerPreCommandesAsync(id)).Single(p => p.NumeroHelloAsso == 194845923);
        Assert.Equal("BREY", brey.Nom);
        Assert.Equal("Anaïs", brey.Prenom);
        Assert.Equal(33.50m, brey.MontantPaye);
        Assert.False(brey.Retiree);
        Assert.Equal(3, brey.Lignes.Single(l => l.NomArticle == "Bretzel").Quantite);
        Assert.Equal(20m, brey.Lignes.Single(l => l.NomArticle == "Tarte flambée gratinée").Montant);
    }

    [Fact]
    public async Task Resynchroniser_ne_cree_rien_en_double_et_garde_les_retraits_et_les_prix()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey()]);

        var brey = (await Service.ListerPreCommandesAsync(id)).Single();
        await Service.MarquerRetireeAsync(brey.Id, true);
        var bretzel = await ProduitAsync(id, "Bretzel");
        bretzel.Prix = 2m;
        bretzel.StockInitial = 100;
        await Service.EnregistrerProduitAsync(bretzel);

        var resultat = await synchro.AppliquerAsync(id, Tarifs, [Brey(), Goepfert()]);

        Assert.Equal(new ResultatSynchro(2, 1, 0, 0), resultat);
        var preCommandes = await Service.ListerPreCommandesAsync(id);
        Assert.Equal(2, preCommandes.Count);
        Assert.True(preCommandes.Single(p => p.NumeroHelloAsso == 194845923).Retiree);
        Assert.Equal(4, (await Service.ProduitsActifsAsync(id)).Count);
        Assert.Equal(2m, (await ProduitAsync(id, "Bretzel")).Prix);
    }

    [Fact]
    public async Task Un_produit_de_meme_nom_deja_saisi_est_relie_et_non_duplique()
    {
        var id = await SaintMartinAsync();
        await Service.AjouterProduitAsync(new Produit { EvenementId = id, Nom = "bretzel", Prix = 1.2m, StockInitial = 50 });

        await synchro.AppliquerAsync(id, Tarifs, []);

        var bretzels = (await Service.ProduitsActifsAsync(id)).Where(p => p.Nom.Equals("bretzel", StringComparison.OrdinalIgnoreCase)).ToList();
        var bretzel = Assert.Single(bretzels);
        Assert.Equal(101, bretzel.TarifHelloAssoId);
        Assert.Equal(1.2m, bretzel.Prix);   // le prix du comptoir n'est pas écrasé
    }

    [Fact]
    public async Task Les_articles_prepayes_sont_deduits_du_stock_retires_ou_non()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey(), Goepfert()]);
        var gratinee = await ProduitAsync(id, "Tarte flambée gratinée");
        gratinee.StockInitial = 10;
        await Service.EnregistrerProduitAsync(gratinee);

        var restant = (await Service.ProduitsEnVenteAsync(id)).Single(p => p.Id == gratinee.Id).StockRestant;
        Assert.Equal(7, restant);

        var brey = (await Service.ListerPreCommandesAsync(id)).Single(p => p.NumeroHelloAsso == 194845923);
        await Service.MarquerRetireeAsync(brey.Id, true);
        Assert.Equal(7, (await Service.ProduitsEnVenteAsync(id)).Single(p => p.Id == gratinee.Id).StockRestant);

        Assert.Equal(3, (await Service.PrevendusAsync(id))[gratinee.Id]);
    }

    [Fact]
    public async Task La_caisse_ne_peut_pas_vendre_les_articles_deja_prepayes()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey()]);
        var nature = await ProduitAsync(id, "Tarte flambée nature");
        nature.StockInitial = 3;
        await Service.EnregistrerProduitAsync(nature);

        var ex = await Assert.ThrowsAsync<StockInsuffisantException>(() =>
            Service.EncaisserAsync(id, new Dictionary<int, int> { [nature.Id] = 3 }));
        Assert.Equal(2, ex.Restant);
    }

    [Fact]
    public async Task Une_commande_remboursee_ensuite_libere_le_stock_et_ne_peut_plus_etre_retiree()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Goepfert()]);
        var bretzel = await ProduitAsync(id, "Bretzel");
        bretzel.StockInitial = 10;
        await Service.EnregistrerProduitAsync(bretzel);
        Assert.Equal(9, (await Service.ProduitsEnVenteAsync(id)).Single(p => p.Id == bretzel.Id).StockRestant);

        var remboursee = Goepfert() with
        {
            Payments = [new PaiementHelloAsso(1, 1150, "Refunded")],
        };
        var resultat = await synchro.AppliquerAsync(id, Tarifs, [remboursee]);

        Assert.Equal(1, resultat.NouvellesAnnulees);
        Assert.Equal(10, (await Service.ProduitsEnVenteAsync(id)).Single(p => p.Id == bretzel.Id).StockRestant);
        var preCommande = (await Service.ListerPreCommandesAsync(id)).Single();
        Assert.True(preCommande.Annulee);
        Assert.Equal(0m, preCommande.MontantPaye);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.MarquerRetireeAsync(preCommande.Id, true));
    }

    [Fact]
    public async Task Un_article_annule_seul_est_retire_de_la_precommande()
    {
        var id = await SaintMartinAsync();
        var commande = Commande(1, "MARTIN", "Léa",
            Article(101, "Bretzel", 150), Article(103, "Tarte flambée gratinée", 1000, etat: "Canceled"));

        await synchro.AppliquerAsync(id, Tarifs, [commande]);

        var preCommande = (await Service.ListerPreCommandesAsync(id)).Single();
        Assert.False(preCommande.Annulee);
        Assert.Equal(1.5m, preCommande.MontantPaye);
        Assert.Equal("Bretzel", Assert.Single(preCommande.Lignes).NomArticle);
    }

    [Fact]
    public async Task Un_simple_don_n_est_pas_une_precommande()
    {
        var id = await SaintMartinAsync();
        var don = Commande(2, "DURAND", "Paul", Article(0, "Don", 500, type: "Donation"));

        var resultat = await synchro.AppliquerAsync(id, Tarifs, [don]);

        Assert.Equal(0, resultat.PreCommandes);
        Assert.Empty(await Service.ListerPreCommandesAsync(id));
    }

    [Fact]
    public async Task Retirer_puis_annuler_le_retrait_previent_les_autres_ecrans()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey()]);
        var signaux = new List<int>();
        Base.Notifications.EvenementModifie += signaux.Add;
        var brey = (await Service.ListerPreCommandesAsync(id)).Single();

        await Service.MarquerRetireeAsync(brey.Id, true);
        Assert.NotNull((await Service.ListerPreCommandesAsync(id)).Single().RetireeLe);

        await Service.MarquerRetireeAsync(brey.Id, false);
        Assert.Null((await Service.ListerPreCommandesAsync(id)).Single().RetireeLe);
        Assert.Equal([id, id], signaux);
    }

    [Fact]
    public async Task Le_recapitulatif_separe_le_prepaye_du_tiroir()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey(), Goepfert()]);
        var brey = (await Service.ListerPreCommandesAsync(id)).Single(p => p.NumeroHelloAsso == 194845923);
        await Service.MarquerRetireeAsync(brey.Id, true);
        var bretzel = await ProduitAsync(id, "Bretzel");
        await Service.EncaisserAsync(id, new Dictionary<int, int> { [bretzel.Id] = 2 });

        var recap = (await Service.RecapitulatifAsync(id))!;

        Assert.Equal(3m, recap.TotalVentes);
        Assert.Equal(53m, recap.TotalEnCaisse);   // fond de caisse + comptoir seulement
        Assert.NotNull(recap.HelloAsso);
        Assert.Equal(45m, recap.HelloAsso.Total);
        Assert.Equal(48m, recap.RecetteTotale);
        Assert.Equal(2, recap.HelloAsso.NombreCommandes);
        Assert.Equal(1, recap.HelloAsso.NombreRetirees);
        Assert.Equal(194863320, Assert.Single(recap.HelloAsso.NonRetirees).NumeroHelloAsso);
        var bretzels = recap.HelloAsso.Articles.Single(a => a.Nom == "Bretzel");
        Assert.Equal((4, 3), (bretzels.Quantite, bretzels.QuantiteRetiree));
    }

    [Fact]
    public async Task Une_buvette_sans_helloasso_n_a_pas_de_resume_helloasso()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var recap = (await Service.RecapitulatifAsync(carte.EvenementId))!;

        Assert.Null(recap.HelloAsso);
        Assert.Equal(recap.TotalVentes, recap.RecetteTotale);
    }

    [Fact]
    public async Task Delier_oublie_les_precommandes_et_libere_le_stock_mais_garde_les_produits()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey()]);

        await synchro.DelierAsync(id);

        Assert.Empty(await Service.ListerPreCommandesAsync(id));
        Assert.Empty(await Service.PrevendusAsync(id));
        var evenement = (await Service.ChargerEvenementAsync(id))!;
        Assert.False(evenement.LieeAHelloAsso);
        Assert.Equal(4, evenement.Produits.Count);
        Assert.All(evenement.Produits, p => Assert.Null(p.TarifHelloAssoId));
    }

    [Fact]
    public async Task Supprimer_un_produit_garde_la_precommande_lisible()
    {
        var id = await SaintMartinAsync();
        await synchro.AppliquerAsync(id, Tarifs, [Brey()]);

        await Service.SupprimerProduitAsync((await ProduitAsync(id, "Bretzel")).Id);

        var ligne = (await Service.ListerPreCommandesAsync(id)).Single().Lignes.Single(l => l.NomArticle == "Bretzel");
        Assert.Null(ligne.ProduitId);
        Assert.Equal(3, ligne.Quantite);
    }

    [Fact]
    public async Task Creer_une_buvette_deux_fois_depuis_la_meme_campagne_rouvre_la_premiere()
    {
        var id = await SaintMartinAsync();
        var campagne = new CampagneHelloAsso("saint-martin", "Event", "Saint-Martin", "Public", null, null);

        // Déjà liée : aucun appel réseau, on retrouve la buvette existante.
        Assert.Equal(id, await synchro.CreerBuvetteAsync(campagne));
    }
}
