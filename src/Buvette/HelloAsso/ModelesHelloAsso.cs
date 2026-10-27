namespace Buvette.HelloAsso;

// Réponses de l'API HelloAsso v5, réduites aux champs utiles à la buvette.
// Les montants HelloAsso sont en centimes.

public record PageHelloAsso<T>(List<T>? Data, PaginationHelloAsso? Pagination);

public record PaginationHelloAsso(int PageSize, int TotalCount, int PageIndex, int TotalPages, string? ContinuationToken);

/// <summary>Une campagne : boutique, billetterie, adhésion… (« form » dans l'API).</summary>
public record CampagneHelloAsso(
    string FormSlug,
    string FormType,
    string? Title,
    string? State,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate)
{
    /// <summary>
    /// Campagnes qui vendent des articles à remettre sur place. Un don ou une adhésion
    /// n'a rien à faire dans une buvette.
    /// </summary>
    public bool VendDesArticles => FormType is "Event" or "Shop";
}

/// <summary>Le détail public d'une campagne : ses tarifs, y compris ceux encore jamais vendus.</summary>
public record FormulaireHelloAsso(string? Title, DateTimeOffset? StartDate, List<TarifHelloAsso>? Tiers);

/// <summary>Un article proposé dans la campagne (« tier » dans l'API).</summary>
public record TarifHelloAsso(long Id, string? Label, int Price, string? TierType)
{
    public decimal Prix => Price / 100m;
}

public record CommandeHelloAsso(
    long Id,
    DateTimeOffset Date,
    PersonneHelloAsso? Payer,
    List<ArticleHelloAsso>? Items,
    MontantHelloAsso? Amount,
    List<PaiementHelloAsso>? Payments)
{
    public decimal Total => (Amount?.Total ?? 0) / 100m;

    /// <summary>
    /// Une commande dont tous les articles sont annulés ou remboursés ne donne rien à
    /// retirer au comptoir. Le remboursement se lit sur les paiements, l'annulation sur
    /// les articles.
    /// </summary>
    public bool EstAnnulee =>
        (Items is { Count: > 0 } items && items.All(a => a.EstAnnule))
        || (Payments is { Count: > 0 } paiements && paiements.All(p => p.State is "Refunded" or "Refunding"));
}

public record PersonneHelloAsso(string? FirstName, string? LastName, string? Email);

public record ArticleHelloAsso(
    long Id,
    string? Name,
    int Amount,
    string? State,
    string? Type,
    long? TierId,
    PersonneHelloAsso? User,
    List<OptionHelloAsso>? Options)
{
    public decimal Montant => Amount / 100m;

    public bool EstAnnule => State is "Canceled" or "Refunded" or "Contested";

    /// <summary>Un don ajouté au panier est de l'argent, pas un article à remettre.</summary>
    public bool EstUnDon => Type is "Donation";

    /// <summary>
    /// Le nom à afficher. HelloAsso n'envoie aucun nom pour un don ajouté au panier, et garde
    /// les espaces tapés en trop autour des noms d'articles dans le back-office.
    /// </summary>
    public string Libelle => Name?.Trim() is { Length: > 0 } nom ? nom : EstUnDon ? "Don" : "(sans nom)";
}

public record OptionHelloAsso(string? Name, int Amount);

public record MontantHelloAsso(int Total);

public record PaiementHelloAsso(long Id, int Amount, string? State);
