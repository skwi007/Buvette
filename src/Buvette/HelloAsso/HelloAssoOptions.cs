namespace Buvette.HelloAsso;

/// <summary>
/// Accès à l'API HelloAsso de l'association. Jamais dans le dépôt : sur le PC, ces valeurs
/// vivent dans les secrets utilisateur .NET ; sur la Freebox, dans <c>/etc/buvette.env</c>.
/// </summary>
public class HelloAssoOptions
{
    public const string Section = "HelloAsso";

    /// <summary>« Client ID » de la page Intégrations et API du back-office HelloAsso.</summary>
    public string? ClientId { get; set; }

    /// <summary>« Client secret » de la même page. Donne accès aux commandes : à protéger.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Identifiant de l'association dans les adresses HelloAsso :
    /// helloasso.com/associations/<b>les-p-tits-coeurs-de-rexa</b>.
    /// </summary>
    public string? Organisation { get; set; }

    /// <summary>Remplacé par https://api.helloasso-sandbox.com pour essayer sur le bac à sable.</summary>
    public string UrlApi { get; set; } = "https://api.helloasso.com";

    public bool EstConfiguree =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(Organisation);
}
