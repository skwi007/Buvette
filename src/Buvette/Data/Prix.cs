using System.Globalization;

namespace Buvette.Data;

/// <summary>
/// Lecture des montants saisis à la main. Un champ HTML de type number renvoie
/// toujours un point décimal, alors que les bénévoles tapent souvent une virgule :
/// les deux doivent être acceptés.
/// </summary>
public static class Prix
{
    /// <summary>
    /// Formate un montant pour l'attribut <c>value</c> d'un champ HTML <c>type="number"</c>.
    /// Ces champs n'acceptent que le point décimal : un « 1,50 » à la française y est
    /// invalide, et le navigateur affiche alors une case vide au lieu du prix.
    /// </summary>
    public static string PourChamp(decimal? valeur) =>
        valeur?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    public static decimal? Analyser(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;

        var texte = saisie.Trim().Replace(',', '.').Replace(" ", "").Replace(" ", "").Replace(" ", "");

        return decimal.TryParse(texte, NumberStyles.Number, CultureInfo.InvariantCulture, out var valeur)
            ? decimal.Round(valeur, 2)
            : null;
    }
}
