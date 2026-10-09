using System.Security.Cryptography;
using System.Text;

namespace Buvette.Data;

/// <summary>
/// Mot de passe partagé protégeant la buvette lorsqu'elle est exposée sur Internet
/// (démonstration hébergée sur la Freebox). Sur le PC du jour J, aucun mot de passe
/// n'est configuré et l'application reste ouverte à tout le réseau local, comme avant.
/// </summary>
public static class AccesProtege
{
    /// <summary>
    /// Compare la saisie au mot de passe attendu. Les espaces autour et la casse sont ignorés :
    /// les tablettes mettent d'office une majuscule au premier mot, et un bénévole ne doit pas
    /// rester bloqué pour ça. Comparaison à temps constant pour ne rien laisser deviner.
    /// </summary>
    public static bool Verifier(string? saisie, string attendu)
    {
        if (string.IsNullOrWhiteSpace(saisie) || string.IsNullOrWhiteSpace(attendu)) return false;

        var a = Encoding.UTF8.GetBytes(saisie.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(attendu.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// N'accepte de rediriger qu'à l'intérieur de l'application, pour qu'un lien piégé
    /// ne puisse pas renvoyer vers un autre site après la connexion.
    /// </summary>
    public static string RetourSur(string? retour) =>
        !string.IsNullOrEmpty(retour) && retour.StartsWith('/') && !retour.StartsWith("//") && !retour.StartsWith("/\\")
            ? retour
            : "/";
}
