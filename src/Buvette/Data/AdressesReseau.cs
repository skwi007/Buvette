namespace Buvette.Data;

/// <summary>Une adresse IPv4 portée par une carte réseau du poste.</summary>
public record CarteReseau(string Nom, string Description, bool Active, bool AvecPasserelle, string Adresse);

/// <summary>
/// Trie les adresses du poste pour ne proposer aux bénévoles que celles qu'une tablette
/// peut réellement joindre. Un PC de bureau en expose facilement six : commutateurs
/// Hyper-V, VPN d'entreprise, adaptateurs Docker… dont aucun n'est accessible depuis
/// le Wi-Fi de la salle des fêtes.
/// </summary>
public static class AdressesReseau
{
    /// <summary>
    /// Cartes à écarter d'après leur description. Le VPN d'entreprise en fait partie :
    /// il a bien une passerelle et répond depuis le poste, mais aucun téléphone du
    /// réseau local ne l'atteindra.
    /// </summary>
    private static readonly string[] CartesVirtuelles =
    [
        "virtual", "vpn", "hyper-v", "vmware", "virtualbox",
        "tap-", "tunnel", "loopback", "pseudo", "docker",
    ];

    public static IReadOnlyList<CarteReseau> Joignables(IEnumerable<CarteReseau> cartes) =>
        cartes.Where(EstJoignable)
              .OrderBy(c => c.Nom, StringComparer.OrdinalIgnoreCase)
              .ToList();

    private static bool EstJoignable(CarteReseau carte)
    {
        if (!carte.Active || !carte.AvecPasserelle) return false;

        // 169.254.x.x : le poste n'a pas obtenu d'adresse, la carte n'est reliée à rien.
        if (carte.Adresse.StartsWith("169.254.", StringComparison.Ordinal)) return false;
        if (carte.Adresse.StartsWith("127.", StringComparison.Ordinal)) return false;

        return !CartesVirtuelles.Any(mot =>
            carte.Description.Contains(mot, StringComparison.OrdinalIgnoreCase));
    }
}
