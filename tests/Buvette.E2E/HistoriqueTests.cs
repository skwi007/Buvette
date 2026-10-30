using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Buvette.E2E;

/// <summary>En fin de journée : récapitulatif, comptage du tiroir, export pour le trésorier.</summary>
[Collection(CollectionBuvette.Nom)]
public class HistoriqueTests(ApplicationBuvette app)
{
    /// <summary>Un événement de 50 € de fond de caisse avec deux ventes : 17 € puis 1,50 €.</summary>
    private async Task<int> JourneeAsync(string nom)
    {
        var id = await app.EvenementAsync(nom, 50m, ("Tarte flambée nature", 8m, null), ("Jus de pomme", 1.5m, null));
        await app.AvecServiceAsync(async service =>
        {
            var produits = (await service.ProduitsActifsAsync(id)).ToDictionary(p => p.Nom, p => p.Id);
            await service.EncaisserAsync(id, new Dictionary<int, int> { [produits["Tarte flambée nature"]] = 2 }, false);
            await service.EncaisserAsync(id, new Dictionary<int, int> { [produits["Jus de pomme"]] = 1 }, false);
            return id;
        });
        return id;
    }

    [Fact]
    public async Task Le_recapitulatif_totalise_les_ventes_et_le_fond_de_caisse()
    {
        var id = await JourneeAsync("Historique récap");
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/historique/{id}");

        await Expect(page.Chiffre("Total des ventes")).ToHaveTextAsync("17,50 €");
        await Expect(page.Chiffre("Attendu en caisse")).ToHaveTextAsync("67,50 €");
        await Expect(page.Chiffre("Commandes")).ToHaveTextAsync("2");
        await Expect(page.Chiffre("Panier moyen")).ToHaveTextAsync("8,75 €");

        var tarte = page.Locator("tbody tr").Filter(new() { HasTextString = "Tarte flambée nature" });
        await Expect(tarte.Locator("td")).ToHaveTextAsync(["Tarte flambée nature", "8,00 €", "2", "16,00 €"]);
    }

    [Theory]
    [InlineData("67.50", "La caisse tombe juste.")]
    [InlineData("60", "Il manque 7,50 €")]
    [InlineData("70", "Excédent de 2,50 €")]
    public async Task Le_comptage_du_tiroir_annonce_l_ecart(string compte, string annonce)
    {
        var id = await JourneeAsync($"Historique comptage {compte}");
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/historique/{id}");

        await page.Locator("#compte").SaisirAsync(compte);
        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer" }).ClickAsync();

        await Expect(page.GetByText("Comptage enregistré.")).ToBeVisibleAsync();
        await Expect(page.GetByText(annonce)).ToBeVisibleAsync();
        await Expect(page.Chiffre("Écart de caisse")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Annuler_une_commande_la_retire_des_totaux()
    {
        var id = await JourneeAsync("Historique annulation");
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/historique/{id}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Afficher" }).ClickAsync();
        var jus = page.Locator("tbody tr").Filter(new() { HasTextString = "1 × Jus de pomme" });
        await jus.GetByRole(AriaRole.Button, new() { Name = "Annuler" }).ClickAsync();
        await jus.GetByRole(AriaRole.Button, new() { Name = "Oui" }).ClickAsync();

        await Expect(jus).ToHaveCountAsync(0);
        await Expect(page.Chiffre("Total des ventes")).ToHaveTextAsync("16,00 €");
        await Expect(page.Chiffre("Commandes")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task L_export_telecharge_un_fichier_lisible_par_Excel()
    {
        var id = await JourneeAsync("Historique export");
        var page = await app.NouvelOngletAsync();
        await page.OuvrirAsync($"/historique/{id}");

        var telechargement = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Link, new() { Name = "Export Excel (CSV)" }).ClickAsync());

        Assert.Equal("buvette-historique-export-2026-09-26.csv", telechargement.SuggestedFilename);
        var chemin = await telechargement.PathAsync();
        var octets = await File.ReadAllBytesAsync(chemin);
        // Le BOM UTF-8 est ce qui fait afficher correctement les accents à Excel.
        Assert.Equal(Encoding.UTF8.GetPreamble(), octets[..3]);
        var contenu = Encoding.UTF8.GetString(octets[3..]);
        Assert.Contains("Tarte flambée nature;8,00;2;16,00;0;0,00;2", contenu);
        Assert.Contains("Total attendu en caisse;;;67,50", contenu);
    }
}
