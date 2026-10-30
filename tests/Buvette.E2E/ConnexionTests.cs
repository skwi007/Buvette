using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>La buvette exposée sur Internet, protégée par mot de passe comme sur la Freebox.</summary>
[Collection(CollectionProtegee.Nom)]
public class ConnexionTests(ApplicationProtegee app)
{
    [Fact]
    public async Task Sans_connexion_toute_page_mene_a_la_connexion_avec_le_logo()
    {
        var page = await app.NouvelOngletAsync();
        await page.GotoAsync("/historique/1");

        await Expect(page).ToHaveURLAsync(new Regex("/connexion"));
        await Expect(page.Locator("link[rel=icon]")).ToHaveAttributeAsync("href", "/logo-ptitscoeurs.png");
        await Expect(page.GetByAltText("Les P'tits Cœurs de Rexa")).ToBeVisibleAsync();

        // Le logo reste public : sans lui, la page de connexion et l'onglet seraient sans image.
        var logo = await page.APIRequest.GetAsync("/logo-ptitscoeurs.png");
        Assert.Equal(200, logo.Status);
        Assert.Equal("image/png", logo.Headers["content-type"]);
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_est_refuse()
    {
        var page = await app.NouvelOngletAsync();
        await page.GotoAsync("/");

        await page.GetByLabel("Mot de passe").FillAsync("bretzel");
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrer" }).ClickAsync();

        await Expect(page.Locator(".alert-danger")).ToHaveTextAsync("Mot de passe incorrect.");
        await Expect(page).ToHaveURLAsync(new Regex("/connexion"));
    }

    [Fact]
    public async Task Le_bon_mot_de_passe_ouvre_la_page_demandee_puis_on_peut_se_deconnecter()
    {
        var id = await app.EvenementAsync("Buvette en ligne");
        var page = await app.NouvelOngletAsync();
        await page.GotoAsync($"/evenement/{id}");

        await page.GetByLabel("Mot de passe").FillAsync(ApplicationProtegee.Secret);
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrer" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex($"/evenement/{id}$"));
        await Expect(page.Locator("h1")).ToHaveTextAsync("Buvette en ligne");

        await page.GetByRole(AriaRole.Link, new() { Name = "Se déconnecter" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/connexion"));

        await page.GotoAsync($"/evenement/{id}");
        await Expect(page).ToHaveURLAsync(new Regex("/connexion"));
    }
}
