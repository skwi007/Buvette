using Microsoft.EntityFrameworkCore;

namespace Buvette.Data;

/// <summary>Ligne du récapitulatif de fin de journée : un produit, ce qui en a été vendu.</summary>
public record LigneRecap(string Nom, decimal PrixUnitaire, int Quantite, decimal Montant);

/// <summary>Récapitulatif complet des ventes d'un événement.</summary>
public record Recapitulatif(
    Evenement Evenement,
    IReadOnlyList<LigneRecap> Produits,
    int NombreCommandes,
    decimal TotalVentes)
{
    /// <summary>Ce qui doit se trouver physiquement dans la caisse : fond de caisse + recettes.</summary>
    public decimal TotalEnCaisse => Evenement.FondDeCaisse + TotalVentes;

    public decimal PanierMoyen => NombreCommandes == 0 ? 0m : TotalVentes / NombreCommandes;
}

public class BuvetteService(IDbContextFactory<BuvetteContext> factory)
{
    public async Task<List<Evenement>> ListerEvenementsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Evenements
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .ToListAsync();
    }

    public async Task<Evenement?> ChargerEvenementAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Evenements
            .Include(e => e.Produits)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>Produits encore en vente, dans l'ordre d'affichage de la caisse.</summary>
    public async Task<List<Produit>> ProduitsActifsAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Produits
            .Where(p => p.EvenementId == evenementId && p.Actif)
            .OrderBy(p => p.Ordre).ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<int> CreerEvenementAsync(Evenement evenement, int? copierProduitsDepuis = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Evenements.Add(evenement);
        await db.SaveChangesAsync();

        if (copierProduitsDepuis is int source)
        {
            var modeles = await db.Produits
                .Where(p => p.EvenementId == source)
                .OrderBy(p => p.Ordre).ThenBy(p => p.Id)
                .ToListAsync();

            db.Produits.AddRange(modeles.Select(p => new Produit
            {
                EvenementId = evenement.Id,
                Nom = p.Nom,
                Prix = p.Prix,
                Ordre = p.Ordre,
                Actif = p.Actif,
            }));
            await db.SaveChangesAsync();
        }

        return evenement.Id;
    }

    // Les mises à jour ciblent explicitement les colonnes modifiées : les entités affichées
    // arrivent avec leurs navigations chargées, et un Update() classique réécrirait tout le graphe.
    public async Task EnregistrerEvenementAsync(Evenement evenement)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Evenements
            .Where(e => e.Id == evenement.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Nom, evenement.Nom)
                .SetProperty(e => e.Date, evenement.Date)
                .SetProperty(e => e.FondDeCaisse, evenement.FondDeCaisse)
                .SetProperty(e => e.Cloture, evenement.Cloture));
    }

    public async Task SupprimerEvenementAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Evenements.Where(e => e.Id == id).ExecuteDeleteAsync();
    }

    public async Task AjouterProduitAsync(Produit produit)
    {
        await using var db = await factory.CreateDbContextAsync();
        var dernier = await db.Produits
            .Where(p => p.EvenementId == produit.EvenementId)
            .MaxAsync(p => (int?)p.Ordre) ?? 0;
        produit.Ordre = dernier + 1;
        db.Produits.Add(produit);
        await db.SaveChangesAsync();
    }

    public async Task EnregistrerProduitAsync(Produit produit)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Produits
            .Where(p => p.Id == produit.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Nom, produit.Nom)
                .SetProperty(p => p.Prix, produit.Prix)
                .SetProperty(p => p.Actif, produit.Actif));
    }

    public async Task SupprimerProduitAsync(int produitId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Produits.Where(p => p.Id == produitId).ExecuteDeleteAsync();
    }

    /// <summary>Échange la position de deux produits pour réordonner les boutons de la caisse.</summary>
    public async Task DeplacerProduitAsync(int produitId, int sens)
    {
        await using var db = await factory.CreateDbContextAsync();
        var produit = await db.Produits.FindAsync(produitId);
        if (produit is null) return;

        var fratrie = await db.Produits
            .Where(p => p.EvenementId == produit.EvenementId)
            .OrderBy(p => p.Ordre).ThenBy(p => p.Id)
            .ToListAsync();

        var index = fratrie.FindIndex(p => p.Id == produitId);
        var cible = index + sens;
        if (index < 0 || cible < 0 || cible >= fratrie.Count) return;

        (fratrie[index], fratrie[cible]) = (fratrie[cible], fratrie[index]);
        for (var i = 0; i < fratrie.Count; i++) fratrie[i].Ordre = i + 1;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Encaisse une commande. Les prix sont relus en base au moment de la validation
    /// pour qu'un écran de caisse resté ouvert ne puisse pas vendre à un prix périmé.
    /// </summary>
    public async Task<Commande> EncaisserAsync(int evenementId, IReadOnlyDictionary<int, int> quantites)
    {
        await using var db = await factory.CreateDbContextAsync();

        var evenement = await db.Evenements.FirstOrDefaultAsync(e => e.Id == evenementId)
            ?? throw new InvalidOperationException("Cet événement n'existe plus.");
        if (evenement.Cloture)
            throw new InvalidOperationException("Cet événement est clôturé : plus aucune vente n'est possible.");

        var ids = quantites.Where(q => q.Value > 0).Select(q => q.Key).ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("La commande est vide.");

        var produits = await db.Produits
            .Where(p => p.EvenementId == evenementId && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var commande = new Commande { EvenementId = evenementId, DateHeure = DateTime.Now };

        foreach (var id in ids)
        {
            if (!produits.TryGetValue(id, out var produit))
                throw new InvalidOperationException("Un produit de la commande a été supprimé entre-temps. Vérifiez la commande.");

            commande.Lignes.Add(new LigneCommande
            {
                ProduitId = produit.Id,
                NomProduit = produit.Nom,
                PrixUnitaire = produit.Prix,
                Quantite = quantites[id],
            });
        }

        commande.Total = commande.Lignes.Sum(l => l.SousTotal);

        db.Commandes.Add(commande);
        await db.SaveChangesAsync();
        return commande;
    }

    public async Task<List<Commande>> ListerCommandesAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Commandes
            .Where(c => c.EvenementId == evenementId)
            .Include(c => c.Lignes)
            .OrderByDescending(c => c.DateHeure).ThenByDescending(c => c.Id)
            .AsSplitQuery()
            .ToListAsync();
    }

    /// <summary>Annule une commande saisie par erreur : elle disparaît aussi des totaux.</summary>
    public async Task AnnulerCommandeAsync(int commandeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Commandes.Where(c => c.Id == commandeId).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Agrège les ventes par produit. Les montants sont additionnés en mémoire :
    /// SQLite stocke les décimaux en texte et ne sait pas les sommer sans perte.
    /// </summary>
    public async Task<Recapitulatif?> RecapitulatifAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var evenement = await db.Evenements
            .Include(e => e.Produits)
            .FirstOrDefaultAsync(e => e.Id == evenementId);
        if (evenement is null) return null;

        var lignes = await db.Lignes
            .Where(l => l.Commande!.EvenementId == evenementId)
            .ToListAsync();

        // Un même produit vendu à deux prix différents (tarif corrigé en cours de journée)
        // apparaît sur deux lignes distinctes, pour que les totaux restent vérifiables.
        var recap = lignes
            .GroupBy(l => (l.NomProduit, l.PrixUnitaire))
            .Select(g => new LigneRecap(
                g.Key.NomProduit,
                g.Key.PrixUnitaire,
                g.Sum(l => l.Quantite),
                g.Sum(l => l.SousTotal)))
            .OrderByDescending(r => r.Montant)
            .ThenBy(r => r.Nom)
            .ToList();

        var nbCommandes = await db.Commandes.CountAsync(c => c.EvenementId == evenementId);

        return new Recapitulatif(evenement, recap, nbCommandes, recap.Sum(r => r.Montant));
    }
}
