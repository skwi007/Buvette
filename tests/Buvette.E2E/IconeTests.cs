using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>L'onglet du navigateur porte le logo de l'association, sur toutes les pages.</summary>
[Collection(CollectionBuvette.Nom)]
public class IconeTests(ApplicationBuvette app)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/helloasso")]
    [InlineData("/page-qui-n-existe-pas")]
    public async Task L_onglet_affiche_le_logo_de_l_association(string chemin)
    {
        var page = await app.NouvelOngletAsync();
        await page.GotoAsync(chemin);

        var icone = page.Locator("link[rel=icon]");
        await Expect(icone).ToHaveAttributeAsync("href", "logo-ptitscoeurs.png");
        await Expect(page.Locator("img.logo")).ToHaveAttributeAsync("src", "logo-ptitscoeurs.png");

        // L'adresse doit répondre par une vraie image, sans quoi le navigateur garde une icône vide.
        var href = await icone.EvaluateAsync<string>("l => l.href");
        var reponse = await page.APIRequest.GetAsync(href);
        Assert.Equal(200, reponse.Status);
        Assert.Equal("image/png", reponse.Headers["content-type"]);
    }
}
