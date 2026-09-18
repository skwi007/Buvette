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

    public List<Produit> Produits { get; set; } = [];
    public List<Commande> Commandes { get; set; } = [];
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

    /// <summary>Position du bouton sur l'écran de caisse.</summary>
    public int Ordre { get; set; }

    /// <summary>Produit retiré de la vente (épuisé) mais conservé dans l'historique.</summary>
    public bool Actif { get; set; } = true;
}

/// <summary>Un passage en caisse : ce qu'une personne a acheté en une fois.</summary>
public class Commande
{
    public int Id { get; set; }
    public int EvenementId { get; set; }
    public Evenement? Evenement { get; set; }

    public DateTime DateHeure { get; set; } = DateTime.Now;

    /// <summary>Total recalculé à l'encaissement et figé ici.</summary>
    public decimal Total { get; set; }

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
