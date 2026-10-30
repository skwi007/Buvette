using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>Le jour J, au comptoir : composer la commande, encaisser, rendre la monnaie.</summary>
[Collection(CollectionBuvette.Nom)]
public class CaisseTests(ApplicationBuvette app)
{
    [Fact]
    public async Task Encaisser_une_commande_annonce_la_monnaie_a_rendre()
    {
        var id = await app.EvenementAsync("Caisse rendu", 50m,
            ("Tarte flambée nature", 8m, null), ("Jus de pomme", 1.5m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/caisse/{id}");

        await page.Produit("Tarte flambée nature").ClickAsync();
        await page.Produit("Tarte flambée nature").ClickAsync();
        await page.Produit("Jus de pomme").ClickAsync();

        await Expect(page.Produit("Tarte flambée nature").Locator(".produit-badge")).ToHaveTextAsync("2");
        await Expect(page.Locator(".total")).ToHaveTextAsync("17,50 €");

        await page.Locator("#recu").SaisirAsync("20");
        await Expect(page.Locator(".panier .alert-info")).ToContainTextAsync("À rendre : 2,50 €");

        await page.GetByRole(AriaRole.Button, new() { Name = "Encaisser" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("encaissée : 17,50 €. À rendre : 2,50 €.");
        // Le comptoir est prêt pour le client suivant.
        await Expect(page.Locator(".panier")).ToContainTextAsync("Touchez un produit pour commencer la commande.");
        await Expect(page.Locator(".total")).ToHaveTextAsync("0,00 €");
    }

    [Fact]
    public async Task Les_boutons_plus_et_moins_ajustent_la_commande()
    {
        var id = await app.EvenementAsync("Caisse quantités", 0m, ("Bretzel", 1m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/caisse/{id}");

        await page.Produit("Bretzel").ClickAsync();
        await page.GetByLabel("Ajouter un Bretzel").ClickAsync();
        await page.GetByLabel("Ajouter un Bretzel").ClickAsync();
        await Expect(page.Locator(".total")).ToHaveTextAsync("3,00 €");

        await page.GetByLabel("Retirer un Bretzel").ClickAsync();
        await Expect(page.Locator(".total")).ToHaveTextAsync("2,00 €");

        await page.GetByRole(AriaRole.Button, new() { Name = "Tout effacer" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Encaisser" })).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Un_montant_recu_insuffisant_est_signale()
    {
        var id = await app.EvenementAsync("Caisse manque", 0m, ("Tarte flambée gratinée", 9m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/caisse/{id}");

        await page.Produit("Tarte flambée gratinée").ClickAsync();
        await page.Locator("#recu").SaisirAsync("5");

        await Expect(page.Locator(".panier .alert-warning")).ToContainTextAsync("Il manque 4,00 €.");
    }

    [Fact]
    public async Task Une_commande_offerte_n_entre_pas_dans_la_recette()
    {
        var id = await app.EvenementAsync("Caisse offert", 0m, ("Café", 1.2m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/caisse/{id}");

        await page.Produit("Café").ClickAsync();
        await page.GetByLabel("Offert").CheckAsync();
        await Expect(page.Locator(".panier")).ToContainTextAsync("Valeur offerte");
        await page.GetByRole(AriaRole.Button, new() { Name = "Valider l'offert" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("offerte : 1,20 € non encaissés.");
        // « Offert » ne doit jamais rester coché pour le client suivant.
        await Expect(page.GetByLabel("Offert")).Not.ToBeCheckedAsync();

        await page.OuvrirAsync($"/historique/{id}");
        await Expect(page.Chiffre("Total des ventes")).ToHaveTextAsync("0,00 €");
        await Expect(page.Chiffre("Offert")).ToHaveTextAsync("1,20 €");
    }

    [Fact]
    public async Task Un_produit_dont_le_stock_est_vendu_devient_epuise()
    {
        var id = await app.EvenementAsync("Caisse stock", 0m, ("Part de gâteau", 2m, 2));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/caisse/{id}");

        var gateau = page.Produit("Part de gâteau");
        await Expect(gateau.Locator(".produit-stock")).ToHaveTextAsync("Plus que 2");

        await gateau.ClickAsync();
        await Expect(gateau.Locator(".produit-stock")).ToHaveTextAsync("Plus que 1");
        await gateau.ClickAsync();
        await Expect(gateau.Locator(".produit-stock")).ToHaveTextAsync("Épuisé");
        await Expect(gateau).ToBeDisabledAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Encaisser" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("encaissée : 4,00 €");
        await Expect(gateau).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Une_vente_sur_une_caisse_met_a_jour_les_autres_ecrans_sans_recharger()
    {
        var id = await app.EvenementAsync("Caisse temps réel", 0m, ("Bouteille d'eau", 1m, 10));
        var comptoir = await app.NouvelOngletAsync();
        var tablette = await app.NouvelOngletAsync();
        var tresorier = await app.NouvelOngletAsync();
        await comptoir.OuvrirAsync($"/caisse/{id}");
        await tablette.OuvrirAsync($"/caisse/{id}");
        await tresorier.OuvrirAsync($"/historique/{id}");

        await comptoir.Produit("Bouteille d'eau").ClickAsync();
        await comptoir.Produit("Bouteille d'eau").ClickAsync();
        await comptoir.Produit("Bouteille d'eau").ClickAsync();
        await comptoir.GetByRole(AriaRole.Button, new() { Name = "Encaisser" }).ClickAsync();

        await Expect(tablette.Produit("Bouteille d'eau").Locator(".produit-stock")).ToHaveTextAsync("Plus que 7");
        await Expect(tresorier.Chiffre("Total des ventes")).ToHaveTextAsync("3,00 €");
        await Expect(tresorier.Chiffre("Commandes")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Un_evenement_inconnu_est_annonce_comme_introuvable()
    {
        var page = await app.NouvelOngletAsync();
        await page.GotoAsync("/caisse/999999");

        await Expect(page.Locator("h1")).ToHaveTextAsync("Événement introuvable");
    }
}
