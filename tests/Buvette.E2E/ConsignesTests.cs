using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>Gobelets consignés : la consigne s'ajoute au prix et se rend au retour du verre.</summary>
[Collection(CollectionBuvette.Nom)]
public class ConsignesTests(ApplicationBuvette app)
{
    [Fact]
    public async Task La_consigne_de_l_evenement_s_ajoute_au_produit_consigne_puis_se_rembourse()
    {
        var id = await app.EvenementAsync("Consigne bière", 50m, ("Bière", 2m, null));
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/evenement/{id}");
        await page.Locator("#ev-consigne").FillAsync("1");
        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer" }).ClickAsync();
        await Expect(page.GetByText("Réglages enregistrés.")).ToBeVisibleAsync();
        await page.GetByLabel("Bière consigné").CheckAsync();
        await Expect(page.Locator("thead")).ToContainTextAsync("+ 1,00 €");

        await page.OuvrirAsync($"/caisse/{id}");
        await Expect(page.Produit("Bière")).ToContainTextAsync("+ 1,00 € consigne");
        // Aucun gobelet n'est encore sorti : rien à rembourser.
        await Expect(page.Produit("Verre rendu")).ToBeDisabledAsync();

        await page.Produit("Bière").ClickAsync();
        await Expect(page.Locator(".total")).ToHaveTextAsync("3,00 €");
        await page.GetByRole(AriaRole.Button, new() { Name = "Encaisser" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("encaissée : 3,00 €");

        // Le client revient avec son gobelet.
        await page.Produit("Verre rendu").ClickAsync();
        await Expect(page.Locator(".total")).ToHaveTextAsync("1,00 €");
        await Expect(page.Locator(".panier")).ToContainTextAsync("À rembourser au client");
        await page.GetByRole(AriaRole.Button, new() { Name = "Rembourser les verres" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1,00 € rendus au client");
        await Expect(page.Produit("Verre rendu")).ToBeDisabledAsync();

        await page.OuvrirAsync($"/historique/{id}");
        await Expect(page.Chiffre("Total des ventes")).ToHaveTextAsync("2,00 €");
        await Expect(page.Chiffre("Attendu en caisse")).ToHaveTextAsync("52,00 €");
        await Expect(page.Chiffre("Verres non rendus")).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Sur_telephone_les_cases_de_prix_occupent_la_largeur_de_la_carte()
    {
        var id = await app.EvenementAsync("Consigne mobile", 0m, ("Vin blanc", 3m, null));
        var contexte = await app.Navigateur.NewContextAsync(new()
        {
            BaseURL = app.Adresse,
            Locale = "fr-FR",
            ViewportSize = new() { Width = 390, Height = 844 },
        });
        var page = await contexte.NewPageAsync();
        await page.OuvrirAsync($"/evenement/{id}");

        var prix = await page.GetByLabel("Prix de Vin blanc").BoundingBoxAsync();
        var stock = await page.GetByLabel("Stock de Vin blanc").BoundingBoxAsync();

        // Deux cases par ligne sur un écran de 390 px : chacune garde plus de 120 px.
        Assert.True(prix!.Width > 120, $"case de prix large de {prix.Width} px");
        Assert.True(stock!.Width > 120, $"case de stock large de {stock.Width} px");

        var defilement = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(defilement, "la page ne doit pas défiler en largeur");
    }
}
