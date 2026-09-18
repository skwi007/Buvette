using System.Globalization;

namespace Buvette.Data;

/// <summary>
/// Lecture des montants saisis à la main. Un champ HTML de type number renvoie
/// toujours un point décimal, alors que les bénévoles tapent souvent une virgule :
/// les deux doivent être acceptés.
/// </summary>
public static class Prix
{
    public static decimal? Analyser(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;

        var texte = saisie.Trim().Replace(',', '.').Replace(" ", "").Replace(" ", "").Replace(" ", "");

        return decimal.TryParse(texte, NumberStyles.Number, CultureInfo.InvariantCulture, out var valeur)
            ? decimal.Round(valeur, 2)
            : null;
    }
}
