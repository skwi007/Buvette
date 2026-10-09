using System.ComponentModel.DataAnnotations;

namespace Buvette.Data;

/// <summary>Une journée de buvette : fête de l'école, tournoi, marché de Noël...</summary>
public class Evenement
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom de l'événement est obligatoire.")]
    [MaxLength(120)]
    public string Nom { get; set; } = "";

    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Monnaie placée dans la caisse avant l'ouverture, pour rendre la monnaie.</summary>
    [Range(0, 100000, ErrorMessage = "Le fond de caisse doit être positif.")]
    public decimal FondDeCaisse { get; set; }

    /// <summary>Un événement clôturé n'accepte plus de nouvelles ventes.</summary>
    public bool Cloture { get; set; }

    /// <summary>
    /// Ce qui a réellement été compté dans le tiroir en fin de journée, ou <c>null</c>
    /// tant que le comptage n'a pas été fait. Conservé pour garder trace de l'écart.
    /// </summary>
    [Range(0, 1000000, ErrorMessage = "Le montant compté doit être positif.")]
    public decimal? MontantCompte { get; set; }

    /// <summary>
    /// Type de la campagne HelloAsso liée (« Event », « Shop »…), ou <c>null</c> pour une
    /// buvette sans prévente. L'API désigne une campagne par ce type et son identifiant.
    /// </summary>
    [MaxLength(40)]
    public string? HelloAssoType { get; set; }

    /// <summary>Identifiant de la campagne dans son adresse HelloAsso (« saint-martin »).</summary>
    [MaxLength(200)]
    public string? HelloAssoCampagne { get; set; }

    /// <summary>Dernière synchronisation réussie, pour savoir si la liste est à jour.</summary>
    public DateTime? HelloAssoSynchroLe { get; set; }

    public bool LieeAHelloAsso => HelloAssoType is not null && HelloAssoCampagne is not null;

    public List<Produit> Produits { get; set; } = [];
    public List<Commande> Commandes { get; set; } = [];
    public List<PreCommande> PreCommandes { get; set; } = [];
}

/// <summary>Un article vendu à la buvette, avec son prix pour cet événement.</summary>
public class Produit
{
    public int Id { get; set; }
    public int EvenementId { get; set; }
    public Evenement? Evenement { get; set; }

    [Required(ErrorMessage = "Le nom du produit est obligatoire.")]
    [MaxLength(120)]
    public string Nom { get; set; } = "";

    [Range(0, 10000, ErrorMessage = "Le prix doit être compris entre 0 et 10 000 €.")]
    public decimal Prix { get; set; }

    /// <summary>
    /// Quantité prévue pour la journée, ou <c>null</c> quand le produit n'est pas compté
    /// (bouteilles d'eau réapprovisionnées au fil de l'eau, par exemple).
    /// Le restant n'est pas stocké : il se déduit des ventes, si bien qu'annuler une
    /// commande erronée remet automatiquement les articles à disposition.
    /// </summary>
    [Range(0, 100000, ErrorMessage = "Le stock doit être un nombre positif.")]
    public int? StockInitial { get; set; }

    /// <summary>Position du bouton sur l'écran de caisse.</summary>
    public int Ordre { get; set; }

    /// <summary>Produit retiré de la vente (épuisé) mais conservé dans l'historique.</summary>
    public bool Actif { get; set; } = true;

    /// <summary>
    /// Tarif HelloAsso vendu sous ce produit, ou <c>null</c> pour un produit vendu seulement
    /// au comptoir. On s'appuie sur l'identifiant et non sur le libellé : HelloAsso laisse
    /// traîner des espaces en fin de libellé, et l'association peut renommer le produit ici.
    /// </summary>
    public long? TarifHelloAssoId { get; set; }
}

/// <summary>Un passage en caisse : ce qu'une personne a acheté en une fois.</summary>
public class Commande
{
    public int Id { get; set; }
    public int EvenementId { get; set; }
    public Evenement? Evenement { get; set; }

    public DateTime DateHeure { get; set; } = DateTime.Now;

    /// <summary>
    /// Valeur de la commande, recalculée à l'encaissement et figée ici. Renseignée même
    /// pour une commande offerte, afin de savoir ce que les gratuités ont représenté.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Commande offerte (bénévoles, organisateurs) : elle consomme le stock mais
    /// n'entre pas dans la recette, et n'a donc pas à se retrouver dans le tiroir.
    /// </summary>
    public bool Offerte { get; set; }

    public List<LigneCommande> Lignes { get; set; } = [];
}

/// <summary>
/// Une ligne d'une commande. Le nom et le prix sont recopiés au moment de la vente :
/// modifier ou supprimer un produit ensuite ne fausse pas l'historique.
/// </summary>
public class LigneCommande
{
    public int Id { get; set; }
    public int CommandeId { get; set; }
    public Commande? Commande { get; set; }

    public int? ProduitId { get; set; }
    public Produit? Produit { get; set; }

    [MaxLength(120)]
    public string NomProduit { get; set; } = "";

    public decimal PrixUnitaire { get; set; }
    public int Quantite { get; set; }

    public decimal SousTotal => PrixUnitaire * Quantite;
}

/// <summary>
/// Une commande payée d'avance sur HelloAsso, à remettre au comptoir. Ce n'est pas une
/// <see cref="Commande"/> : son argent n'est jamais passé par le tiroir.
/// </summary>
/// <remarks>
/// Ses articles sortent du stock dès la synchronisation, retirés ou non : ils sont payés,
/// et la caisse ne doit pas les revendre à quelqu'un d'autre.
/// </remarks>
public class PreCommande
{
    public int Id { get; set; }
    public int EvenementId { get; set; }
    public Evenement? Evenement { get; set; }

    /// <summary>Numéro de commande HelloAsso, celui que le client a reçu par e-mail.</summary>
    public long NumeroHelloAsso { get; set; }

    [MaxLength(120)]
    public string Nom { get; set; } = "";

    [MaxLength(120)]
    public string Prenom { get; set; } = "";

    public DateTime DateCommande { get; set; }

    /// <summary>Ce qui a été payé sur HelloAsso pour les articles non annulés.</summary>
    public decimal MontantPaye { get; set; }

    /// <summary>Remboursée ou annulée sur HelloAsso : rien à remettre, rien de réservé.</summary>
    public bool Annulee { get; set; }

    /// <summary>Heure de remise au client, ou <c>null</c> tant qu'il n'est pas passé.</summary>
    public DateTime? RetireeLe { get; set; }

    public List<LignePreCommande> Lignes { get; set; } = [];

    public bool Retiree => RetireeLe is not null;
}

/// <summary>Un article d'une pré-commande, regroupé par tarif HelloAsso.</summary>
public class LignePreCommande
{
    public int Id { get; set; }
    public int PreCommandeId { get; set; }
    public PreCommande? PreCommande { get; set; }

    public int? ProduitId { get; set; }
    public Produit? Produit { get; set; }

    /// <summary>Libellé HelloAsso, recopié pour rester lisible si le produit est supprimé.</summary>
    [MaxLength(200)]
    public string NomArticle { get; set; } = "";

    public int Quantite { get; set; }
    public decimal Montant { get; set; }
}
