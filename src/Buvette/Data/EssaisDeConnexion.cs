using System.Net;

namespace Buvette.Data;

/// <summary>Issue d'un essai de mot de passe.</summary>
/// <param name="Accepte">Le mot de passe est bon : l'appareil est connecté.</param>
/// <param name="Attente">
/// Non nulle quand l'essai est bloqué sans même que le mot de passe soit vérifié : le temps
/// à attendre avant de pouvoir réessayer.
/// </param>
public readonly record struct IssueEssai(bool Accepte, TimeSpan Attente)
{
    public bool Bloque => Attente > TimeSpan.Zero;
}

/// <summary>
/// Arrête les essais de mot de passe en boucle. La seconde de pénalité ne freine qu'un robot
/// qui essaie un mot de passe après l'autre : envoyés en parallèle, cent essais passeraient
/// dans la même seconde. Après <see cref="ErreursParAdresse"/> erreurs en un quart d'heure,
/// une adresse IP doit donc attendre, et son mot de passe n'est même plus vérifié. Le plafond
/// <see cref="ErreursAuTotal"/>, commun à toutes les adresses, arrête aussi un robot qui en
/// changerait : seules les nouvelles connexions sont alors refusées, les appareils déjà
/// connectés gardent leur cookie.
/// </summary>
/// <remarks>
/// Une connexion réussie efface les erreurs de son adresse : un bénévole qui s'est trompé
/// repart de zéro. Les erreurs ne sont comptées qu'en mémoire : redémarrer l'application les
/// oublie, ce qui débloque tout le monde.
/// </remarks>
public class EssaisDeConnexion(TimeProvider horloge)
{
    public const int ErreursParAdresse = 10;
    public const int ErreursAuTotal = 50;
    public static readonly TimeSpan Fenetre = TimeSpan.FromMinutes(15);

    private readonly Lock verrou = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> parAdresse = [];
    private readonly Queue<DateTimeOffset> toutes = new();

    public IssueEssai Essayer(IPAddress? adresse, string? saisie, string attendu)
    {
        var maintenant = horloge.GetUtcNow();
        var cle = adresse?.ToString() ?? "";

        // Tout se joue sous le verrou, vérification comprise : des essais simultanés ne
        // peuvent pas passer avant que les erreurs des précédents soient comptées.
        lock (verrou)
        {
            Oublier(maintenant - Fenetre);

            var erreurs = parAdresse.GetValueOrDefault(cle);
            if (erreurs is { Count: >= ErreursParAdresse }) return Bloque(erreurs.Peek(), maintenant);
            if (toutes.Count >= ErreursAuTotal) return Bloque(toutes.Peek(), maintenant);

            if (AccesProtege.Verifier(saisie, attendu))
            {
                parAdresse.Remove(cle);
                return new IssueEssai(true, TimeSpan.Zero);
            }

            if (erreurs is null) parAdresse[cle] = erreurs = new Queue<DateTimeOffset>();
            erreurs.Enqueue(maintenant);
            toutes.Enqueue(maintenant);
            return new IssueEssai(false, TimeSpan.Zero);
        }
    }

    /// <summary>Bloqué jusqu'à ce que la plus ancienne erreur sorte de la fenêtre.</summary>
    private static IssueEssai Bloque(DateTimeOffset plusAncienne, DateTimeOffset maintenant) =>
        new(false, plusAncienne + Fenetre - maintenant);

    /// <summary>
    /// Oublie les erreurs sorties de la fenêtre, et les adresses qui n'en ont plus : un robot
    /// qui change d'adresse ne fait pas grossir la mémoire indéfiniment.
    /// </summary>
    private void Oublier(DateTimeOffset avant)
    {
        while (toutes.TryPeek(out var plusAncienne) && plusAncienne <= avant) toutes.Dequeue();

        foreach (var (cle, erreurs) in parAdresse)
        {
            while (erreurs.TryPeek(out var plusAncienne) && plusAncienne <= avant) erreurs.Dequeue();
            if (erreurs.Count == 0) parAdresse.Remove(cle);
        }
    }
}
