using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>Avant le jour J : créer l'événement, saisir la carte, clôturer en fin de journée.</summary>
[Collection(CollectionBuvette.Nom)]
public class PreparationTests(ApplicationBuvette app)
{
    [Fact]
    public async Task Creer_un_evenement_depuis_l_accueil_puis_saisir_ses_produits()
    {
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync("/");

        await page.GetByRole(AriaRole.Button, new() { Name = "+ Nouvel événement" }).ClickAsync();
        await page.Locator("#nom").FillAsync("Marché de Noël");
        await page.Locator("#date").FillAsync("2026-12-12");
        await page.Locator("#fond").SaisirAsync("80");
        // Partir d'une liste vide, même si un autre test a déjà créé un événement.
        if (await page.Locator("#modele").CountAsync() > 0)
            await page.Locator("#modele").SelectOptionAsync("");
        await page.GetByRole(AriaRole.Button, new() { Name = "Créer" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/evenement/\d+$"));
        await Expect(page.Locator("h1")).ToHaveTextAsync("Marché de Noël");
        await Expect(page.Locator("#ev-fond")).ToHaveValueAsync(new Regex(@"^80(\.0+)?$"));

        await AjouterProduitAsync(page, "Vin chaud", "2.5", "40");
        await AjouterProduitAsync(page, "Bredele", "1", "");

        var noms = page.GetByLabel("Nom du produit");
        await Expect(noms).ToHaveCountAsync(2);
        await Expect(noms.Nth(0)).ToHaveValueAsync("Vin chaud");
        await Expect(page.GetByLabel("Prix de Vin chaud")).ToHaveValueAsync("2.5");
        await Expect(page.Locator("tbody tr").Nth(0)).ToContainTextAsync("40 restants");
        await Expect(page.Locator("tbody tr").Nth(1)).ToContainTextAsync("illimité");

        // Retour à l'accueil : l'événement y figure, avec ses accès.
        await page.GetByText("← Tous les événements").ClickAsync();
        var carte = page.Locator(".card").Filter(new() { HasTextString = "Marché de Noël" });
        await Expect(carte).ToContainTextAsync("samedi 12 décembre 2026");
        await Expect(carte.GetByRole(AriaRole.Link, new() { Name = "Caisse" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Modifier_le_prix_d_un_produit_est_enregistre_en_quittant_la_case()
    {
        var id = await app.EvenementAsync("Kermesse prix", 0m, ("Crêpe", 2m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/evenement/{id}");

        await page.GetByLabel("Prix de Crêpe").SaisirAsync("2.5");
        await page.GetByLabel("Nom du produit").SaisirAsync("Crêpe sucre");

        // Rechargement complet : ce qui s'affiche vient bien de la base.
        await page.OuvrirAsync($"/evenement/{id}");
        await Expect(page.GetByLabel("Nom du produit")).ToHaveValueAsync("Crêpe sucre");
        await Expect(page.GetByLabel("Prix de Crêpe sucre")).ToHaveValueAsync("2.5");
    }

    [Fact]
    public async Task Un_produit_retire_de_la_vente_disparait_de_la_caisse()
    {
        var id = await app.EvenementAsync("Kermesse retrait", 0m, ("Café", 1m, null), ("Thé", 1m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/evenement/{id}");

        await page.GetByLabel("Thé en vente").UncheckAsync();

        await page.OuvrirAsync($"/caisse/{id}");
        await Expect(page.Produit("Café")).ToBeVisibleAsync();
        await Expect(page.Produit("Thé")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Une_buvette_cloturee_n_encaisse_plus_et_disparait_des_caisses_de_l_accueil()
    {
        var id = await app.EvenementAsync("Kermesse clôture", 0m, ("Café", 1m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/evenement/{id}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Clôturer la buvette" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Rouvrir la buvette" })).ToBeVisibleAsync();

        await page.OuvrirAsync($"/caisse/{id}");
        await Expect(page.Locator(".alert-warning")).ToContainTextAsync("Cette buvette est clôturée");
        await Expect(page.Produit("Café")).ToBeDisabledAsync();

        await page.OuvrirAsync("/");
        var carte = page.Locator(".card").Filter(new() { HasTextString = "Kermesse clôture" });
        await Expect(carte).ToContainTextAsync("Clôturé");
        await Expect(carte.GetByRole(AriaRole.Link, new() { Name = "Caisse" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Supprimer_un_evenement_demande_confirmation_puis_ramene_a_l_accueil()
    {
        var id = await app.EvenementAsync("Kermesse à supprimer");
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/evenement/{id}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Supprimer l'événement" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Oui, supprimer" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        await Expect(page.GetByText("Kermesse à supprimer")).ToHaveCountAsync(0);

        await page.GotoAsync($"/evenement/{id}");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Événement introuvable");
    }

    private static async Task AjouterProduitAsync(IPage page, string nom, string prix, string stock)
    {
        var lignes = await page.GetByLabel("Nom du produit").CountAsync();
        await page.GetByLabel("Nom du nouveau produit").FillAsync(nom);
        await page.GetByLabel("Prix du nouveau produit").SaisirAsync(prix);
        await page.GetByLabel("Stock du nouveau produit").SaisirAsync(stock);
        await page.GetByRole(AriaRole.Button, new() { Name = "+ Ajouter" }).ClickAsync();
        await Expect(page.GetByLabel("Nom du produit")).ToHaveCountAsync(lignes + 1);
    }
}
