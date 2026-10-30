using Microsoft.Playwright;

namespace Buvette.E2E;

/// <summary>Les tests d'une même collection partagent l'application et s'exécutent l'un après l'autre.</summary>
[CollectionDefinition(Nom)]
public sealed class CollectionBuvette : ICollectionFixture<ApplicationBuvette>
{
    public const string Nom = "Buvette";
}

[CollectionDefinition(Nom)]
public sealed class CollectionProtegee : ICollectionFixture<ApplicationProtegee>
{
    public const string Nom = "Buvette protégée";
}

public static class Outils
{
    /// <summary>
    /// Ouvre une page et attend qu'elle réponde aux clics. Blazor Server affiche d'abord la
    /// page pré-rendue, puis branche ses boutons une fois le circuit établi : un clic arrivé
    /// entre les deux serait perdu sans erreur. Blazor range ses gestionnaires dans une
    /// propriété « _blazorEvents_N » des éléments, c'est elle que l'on guette.
    /// </summary>
    public static async Task OuvrirAsync(this IPage page, string chemin)
    {
        await page.GotoAsync(chemin);
        await page.WaitForFunctionAsync("""
            () => [...document.querySelectorAll('button, input, select')]
                .some(e => Object.keys(e).some(k => k.startsWith('_blazorEvents_')))
            """);
    }

    /// <summary>Saisit une valeur puis quitte la case : Blazor n'enregistre qu'à l'évènement « change ».</summary>
    public static async Task SaisirAsync(this ILocator champ, string valeur)
    {
        await champ.FillAsync(valeur);
        await champ.BlurAsync();
    }

    /// <summary>Le bouton d'un produit sur l'écran de caisse.</summary>
    public static ILocator Produit(this IPage page, string nom) =>
        page.Locator("button.produit-bouton").Filter(new() { Has = page.Locator(".produit-nom", new() { HasTextString = nom }) });

    /// <summary>La valeur d'une carte chiffrée de l'historique (« Total des ventes », « Commandes »…).</summary>
    public static ILocator Chiffre(this IPage page, string libelle) =>
        page.Locator(".chiffre").Filter(new() { HasTextString = libelle }).Locator(".chiffre-valeur");
}
