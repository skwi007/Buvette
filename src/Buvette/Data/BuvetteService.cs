using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Data;

/// <summary>
/// Ligne du récapitulatif de fin de journée : un produit, ce qui en a été vendu et ce
/// qui en a été offert. Les deux sont séparés car seul l'encaissé doit se retrouver
/// dans le tiroir, alors que les deux ont consommé du stock.
/// </summary>
public record LigneRecap(
    string Nom,
    decimal PrixUnitaire,
    int Quantite,
    decimal Montant,
    int QuantiteOfferte,
    decimal MontantOffert)
{
    /// <summary>Tout ce qui est sorti du stock, payé ou non.</summary>
    public int QuantiteTotale => Quantite + QuantiteOfferte;
}

/// <summary>
/// Un produit tel que l'écran de caisse en a besoin : son tarif et ce qu'il en reste.
/// <paramref name="StockRestant"/> vaut <c>null</c> quand le produit n'est pas compté.
/// </summary>
public record ProduitEnVente(int Id, string Nom, decimal Prix, int? StockRestant, decimal? Consigne = null)
{
    public bool AvecConsigne => Consigne is > 0;

    public bool StockSuivi => StockRestant is not null;

    public bool Epuise => StockRestant is 0;

    /// <summary>Seuil à partir duquel la caisse prévient qu'il ne reste plus grand-chose.</summary>
    public bool BientotEpuise => StockRestant is > 0 and <= 5;
}

/// <summary>Récapitulatif complet des ventes d'un événement.</summary>
public record Recapitulatif(
    Evenement Evenement,
    IReadOnlyList<LigneRecap> Produits,
    int NombreCommandes,
    decimal TotalVentes,
    int NombreOffertes,
    decimal TotalOffert,
    ResumeHelloAsso? HelloAsso = null,
    ResumeConsignes? Consignes = null)
{
    /// <summary>
    /// Ce qui doit se trouver physiquement dans la caisse : fond de caisse + recettes du
    /// comptoir + consignes des verres pas encore rapportés. Les préventes HelloAsso n'y
    /// sont pas : leur argent est sur le compte.
    /// </summary>
    public decimal TotalEnCaisse => Evenement.FondDeCaisse + TotalVentes + (Consignes?.Solde ?? 0m);

    /// <summary>Toute la recette de la journée : comptoir et préventes HelloAsso.</summary>
    public decimal RecetteTotale => TotalVentes + (HelloAsso?.Total ?? 0m);

    public decimal PanierMoyen => NombreCommandes == 0 ? 0m : TotalVentes / NombreCommandes;

    /// <summary>Le comptage a-t-il été fait ?</summary>
    public bool Compte => Evenement.MontantCompte is not null;

    /// <summary>
    /// Écart entre le tiroir et l'attendu : positif s'il y a plus que prévu, négatif s'il
    /// manque. <c>null</c> tant que personne n'a compté.
    /// </summary>
    public decimal? Ecart => Evenement.MontantCompte is decimal compte ? compte - TotalEnCaisse : null;

    /// <summary>Un écart de quelques centimes n'a pas à être signalé comme un problème.</summary>
    public bool EcartSignificatif => Ecart is decimal e && Math.Abs(e) >= 0.01m;
}

/// <summary>Un article prévendu sur HelloAsso, pour le récapitulatif.</summary>
public record LigneHelloAsso(string Nom, int Quantite, decimal Montant, int QuantiteRetiree);

/// <summary>
/// Les préventes HelloAsso d'un événement. Les commandes annulées ou remboursées n'y
/// figurent pas. <see cref="NonRetirees"/> liste celles payées mais jamais venues
/// chercher : l'association décide ensuite si elle rembourse.
/// </summary>
public record ResumeHelloAsso(
    int NombreCommandes,
    int NombreRetirees,
    decimal Total,
    IReadOnlyList<LigneHelloAsso> Articles,
    IReadOnlyList<PreCommande> NonRetirees);

/// <summary>Les verres d'un même montant de consigne : combien sont partis, combien sont revenus.</summary>
public record LigneConsigne(decimal MontantUnitaire, int Sortis, int Rendus)
{
    /// <summary>Verres encore chez les clients, dont la consigne est toujours dans le tiroir.</summary>
    public int EnCirculation => Sortis - Rendus;
}

/// <summary>
/// Le mouvement des consignes d'un événement. <see cref="Solde"/> est l'argent des verres
/// non rapportés : il reste dans la caisse, et revient à l'association en fin de journée.
/// </summary>
public record ResumeConsignes(IReadOnlyList<LigneConsigne> Lignes)
{
    public decimal Encaissees => Lignes.Sum(l => l.MontantUnitaire * l.Sortis);
    public decimal Rendues => Lignes.Sum(l => l.MontantUnitaire * l.Rendus);
    public decimal Solde => Encaissees - Rendues;
    public int VerresSortis => Lignes.Sum(l => l.Sortis);
    public int VerresRendus => Lignes.Sum(l => l.Rendus);
    public int VerresEnCirculation => Lignes.Sum(l => l.EnCirculation);
}

/// <summary>
/// La commande demande plus d'articles qu'il n'en reste. Type dédié pour que l'écran
/// de caisse puisse afficher le produit fautif et le restant sans analyser un message.
/// </summary>
public class StockInsuffisantException(string produit, int restant)
    : InvalidOperationException(restant == 0
        ? $"« {produit} » est épuisé."
        : $"Il ne reste que {restant} « {produit} ».")
{
    public string Produit { get; } = produit;
    public int Restant { get; } = restant;
}

/// <remarks>
/// Toute modification d'un événement est signalée à <see cref="NotificationsBuvette"/>
/// une fois enregistrée, pour que les autres caisses se mettent à jour d'elles-mêmes.
/// </remarks>
public class BuvetteService(IDbContextFactory<BuvetteContext> factory, NotificationsBuvette notifications)
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

    /// <summary>Produits en vente avec le stock restant, pour l'écran de caisse.</summary>
    public async Task<List<ProduitEnVente>> ProduitsEnVenteAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var produits = await db.Produits
            .Where(p => p.EvenementId == evenementId && p.Actif)
            .OrderBy(p => p.Ordre).ThenBy(p => p.Id)
            .ToListAsync();

        var vendus = await VendusParProduitAsync(db, evenementId);
        var consigne = await db.Evenements
            .Where(e => e.Id == evenementId)
            .Select(e => e.Consigne)
            .FirstOrDefaultAsync();

        return produits
            .Select(p => new ProduitEnVente(p.Id, p.Nom, p.Prix, Restant(p, vendus), p.Consigne ? consigne : null))
            .ToList();
    }

    /// <summary>
    /// Nombre de verres encore chez les clients pour chaque montant de consigne : la caisse
    /// ne peut pas rembourser plus de verres qu'elle n'en a fait payer.
    /// </summary>
    public async Task<Dictionary<decimal, int>> VerresEnCirculationAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await MouvementsConsignesAsync(db, evenementId))
            .ToDictionary(l => l.MontantUnitaire, l => l.EnCirculation);
    }

    /// <summary>
    /// Verres partis et revenus, par montant de consigne. Calculé en mémoire : SQLite stocke
    /// les décimaux en texte et ne sait ni les comparer ni les sommer sans perte.
    /// </summary>
    private static async Task<List<LigneConsigne>> MouvementsConsignesAsync(BuvetteContext db, int evenementId)
    {
        var sortis = await db.Lignes
            .Where(l => l.Commande!.EvenementId == evenementId)
            .Select(l => new { l.ConsigneUnitaire, l.Quantite })
            .ToListAsync();

        var rendus = await db.RetoursConsignes
            .Where(r => r.Commande!.EvenementId == evenementId)
            .Select(r => new { r.MontantUnitaire, r.Quantite })
            .ToListAsync();

        return sortis.Where(s => s.ConsigneUnitaire > 0).Select(s => s.ConsigneUnitaire)
            .Concat(rendus.Select(r => r.MontantUnitaire))
            .Distinct()
            .Order()
            .Select(montant => new LigneConsigne(
                montant,
                sortis.Where(s => s.ConsigneUnitaire == montant).Sum(s => s.Quantite),
                rendus.Where(r => r.MontantUnitaire == montant).Sum(r => r.Quantite)))
            .ToList();
    }

    /// <summary>Stock restant de chaque produit compté, y compris ceux retirés de la vente.</summary>
    public async Task<Dictionary<int, int>> StocksRestantsAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var produits = await db.Produits
            .Where(p => p.EvenementId == evenementId && p.StockInitial != null)
            .ToListAsync();

        var vendus = await VendusParProduitAsync(db, evenementId);

        return produits.ToDictionary(p => p.Id, p => Restant(p, vendus)!.Value);
    }

    /// <summary>
    /// Tout ce qui est sorti du stock : ventes au comptoir et préventes HelloAsso. Une
    /// prévente compte dès qu'elle est connue, retirée ou non : elle est payée, et la
    /// caisse ne doit pas revendre à un autre client l'article qui l'attend.
    /// </summary>
    private static async Task<Dictionary<int, int>> VendusParProduitAsync(BuvetteContext db, int evenementId)
    {
        var vendus = await db.Lignes
            .Where(l => l.Commande!.EvenementId == evenementId && l.ProduitId != null)
            .GroupBy(l => l.ProduitId!.Value)
            .Select(g => new { ProduitId = g.Key, Quantite = g.Sum(l => l.Quantite) })
            .ToDictionaryAsync(x => x.ProduitId, x => x.Quantite);

        foreach (var (produitId, quantite) in await PrevendusParProduitAsync(db, evenementId))
            vendus[produitId] = vendus.GetValueOrDefault(produitId) + quantite;

        return vendus;
    }

    private static async Task<Dictionary<int, int>> PrevendusParProduitAsync(BuvetteContext db, int evenementId) =>
        await db.LignesPreCommandes
            .Where(l => l.PreCommande!.EvenementId == evenementId && !l.PreCommande.Annulee && l.ProduitId != null)
            .GroupBy(l => l.ProduitId!.Value)
            .Select(g => new { ProduitId = g.Key, Quantite = g.Sum(l => l.Quantite) })
            .ToDictionaryAsync(x => x.ProduitId, x => x.Quantite);

    /// <summary>Quantité de chaque produit déjà payée sur HelloAsso, pour l'écran des produits.</summary>
    public async Task<Dictionary<int, int>> PrevendusAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await PrevendusParProduitAsync(db, evenementId);
    }

    /// <summary>
    /// Jamais négatif : baisser le stock sous ce qui a déjà été vendu doit afficher
    /// « épuisé », pas un nombre négatif incompréhensible au comptoir.
    /// </summary>
    private static int? Restant(Produit produit, IReadOnlyDictionary<int, int> vendus) =>
        produit.StockInitial is int prevu
            ? Math.Max(0, prevu - vendus.GetValueOrDefault(produit.Id))
            : null;

    public async Task<int> CreerEvenementAsync(Evenement evenement, int? copierProduitsDepuis = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        // Les produits copiés gardent leur case « consigné » : sans le montant qui va avec,
        // ils seraient vendus sans consigne alors que les gobelets le sont toujours.
        if (copierProduitsDepuis is int modele && evenement.Consigne is null)
            evenement.Consigne = await db.Evenements
                .Where(e => e.Id == modele)
                .Select(e => e.Consigne)
                .FirstOrDefaultAsync();

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
                Consigne = p.Consigne,
                Ordre = p.Ordre,
                Actif = p.Actif,
                // La quantité prévue se reconduit comme point de départ : c'est en général
                // le meilleur repère pour l'édition suivante, quitte à l'ajuster.
                StockInitial = p.StockInitial,
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
                .SetProperty(e => e.Consigne, evenement.Consigne)
                .SetProperty(e => e.Cloture, evenement.Cloture));
        notifications.Signaler(evenement.Id);
        // MontantCompte est volontairement absent : il n'appartient pas au formulaire de
        // réglages, et l'inclure permettrait à un écran resté ouvert d'écraser un comptage
        // saisi entre-temps depuis la page Historique.
    }

    /// <summary>
    /// Enregistre ce qui a été compté dans le tiroir. <c>null</c> efface le comptage,
    /// par exemple pour le refaire après avoir corrigé une erreur de saisie.
    /// </summary>
    public async Task EnregistrerComptageAsync(int evenementId, decimal? montantCompte)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Evenements
            .Where(e => e.Id == evenementId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.MontantCompte, montantCompte));
        notifications.Signaler(evenementId);
    }

    public async Task SupprimerEvenementAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Evenements.Where(e => e.Id == id).ExecuteDeleteAsync();
        notifications.Signaler(id);
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
        notifications.Signaler(produit.EvenementId);
    }

    public async Task EnregistrerProduitAsync(Produit produit)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Produits
            .Where(p => p.Id == produit.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Nom, produit.Nom)
                .SetProperty(p => p.Prix, produit.Prix)
                .SetProperty(p => p.Consigne, produit.Consigne)
                .SetProperty(p => p.Actif, produit.Actif)
                .SetProperty(p => p.StockInitial, produit.StockInitial));
        notifications.Signaler(produit.EvenementId);
    }

    public async Task SupprimerProduitAsync(int produitId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var evenementId = await db.Produits
            .Where(p => p.Id == produitId)
            .Select(p => (int?)p.EvenementId)
            .FirstOrDefaultAsync();

        await db.Produits.Where(p => p.Id == produitId).ExecuteDeleteAsync();
        if (evenementId is int id) notifications.Signaler(id);
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
        notifications.Signaler(produit.EvenementId);
    }

    /// <summary>
    /// Encaisse une commande. Les prix et les stocks sont relus en base au moment de la
    /// validation, pour qu'un écran de caisse resté ouvert ne vende ni à un prix périmé
    /// ni un article que la caisse voisine vient d'épuiser.
    /// </summary>
    /// <remarks>
    /// Quand deux caisses écrivent en même temps, SQLite refuse celle dont l'instantané est
    /// périmé. Cette erreur technique n'a aucun sens pour un bénévole : on refait alors la
    /// tentative, qui relit les stocks à jour et aboutit soit à la vente, soit à un franc
    /// « épuisé ». Sans cette reprise, une vente pourtant possible échouerait.
    /// </remarks>
    /// <param name="verresRendus">
    /// Verres consignés rapportés pendant ce passage, par montant de consigne. Un client
    /// peut ne faire que rendre ses verres : la commande ne contient alors aucun article.
    /// </param>
    public async Task<Commande> EncaisserAsync(
        int evenementId, IReadOnlyDictionary<int, int> quantites, bool offerte = false,
        IReadOnlyDictionary<decimal, int>? verresRendus = null)
    {
        const int tentativesMax = 5;

        for (var tentative = 1; ; tentative++)
        {
            try
            {
                var commande = await EncaisserUneFoisAsync(evenementId, quantites, offerte, verresRendus);
                notifications.Signaler(evenementId);
                return commande;
            }
            catch (SqliteException ex) when (EstConflitDEcriture(ex) && tentative < tentativesMax)
            {
                // Laisse la caisse concurrente terminer, en espaçant un peu plus à chaque essai.
                await Task.Delay(TimeSpan.FromMilliseconds(15 * tentative));
            }
        }
    }

    /// <summary>Codes SQLite signalant que la base était occupée ou l'instantané périmé.</summary>
    private static bool EstConflitDEcriture(SqliteException ex) =>
        ex.SqliteErrorCode is 5 or 6;   // SQLITE_BUSY, SQLITE_LOCKED

    private async Task<Commande> EncaisserUneFoisAsync(
        int evenementId, IReadOnlyDictionary<int, int> quantites, bool offerte,
        IReadOnlyDictionary<decimal, int>? verresRendus)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var evenement = await db.Evenements.FirstOrDefaultAsync(e => e.Id == evenementId)
            ?? throw new InvalidOperationException("Cet événement n'existe plus.");
        if (evenement.Cloture)
            throw new InvalidOperationException("Cet événement est clôturé : plus aucune vente n'est possible.");

        var ids = quantites.Where(q => q.Value > 0).Select(q => q.Key).ToList();
        var rendus = (verresRendus ?? new Dictionary<decimal, int>())
            .Where(r => r.Value > 0 && r.Key > 0)
            .OrderBy(r => r.Key)
            .ToList();
        if (ids.Count == 0 && rendus.Count == 0)
            throw new InvalidOperationException("La commande est vide.");

        var produits = await db.Produits
            .Where(p => p.EvenementId == evenementId && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var vendus = await VendusParProduitAsync(db, evenementId);
        var commande = new Commande { EvenementId = evenementId, DateHeure = DateTime.Now, Offerte = offerte };

        foreach (var id in ids)
        {
            if (!produits.TryGetValue(id, out var produit))
                throw new InvalidOperationException("Un produit de la commande a été supprimé entre-temps. Vérifiez la commande.");

            var demande = quantites[id];
            if (Restant(produit, vendus) is int restant && demande > restant)
                throw new StockInsuffisantException(produit.Nom, restant);

            commande.Lignes.Add(new LigneCommande
            {
                ProduitId = produit.Id,
                NomProduit = produit.Nom,
                PrixUnitaire = produit.Prix,
                Quantite = demande,
                ConsigneUnitaire = produit.Consigne ? evenement.Consigne ?? 0m : 0m,
            });
        }

        if (rendus.Count > 0)
        {
            // Seuls les verres partis lors des passages précédents peuvent revenir : rembourser
            // plus que ce qui a été encaissé viderait le tiroir d'un argent qui n'y est jamais entré.
            var enCirculation = (await MouvementsConsignesAsync(db, evenementId))
                .ToDictionary(l => l.MontantUnitaire, l => l.EnCirculation);

            foreach (var (montant, nombre) in rendus)
            {
                var disponibles = Math.Max(0, enCirculation.GetValueOrDefault(montant));
                if (nombre > disponibles)
                    throw new InvalidOperationException(disponibles == 0
                        ? $"Aucun verre consigné à {montant:C2} n'a été vendu sans être rendu : rien à rembourser."
                        : $"Seulement {disponibles} verre{(disponibles > 1 ? "s" : "")} consigné{(disponibles > 1 ? "s" : "")} à {montant:C2} "
                          + $"{(disponibles > 1 ? "sont" : "est")} encore chez les clients, pas {nombre}.");

                commande.Retours.Add(new RetourConsigne { MontantUnitaire = montant, Quantite = nombre });
            }
        }

        commande.Total = commande.Lignes.Sum(l => l.SousTotal);
        commande.Consignes = commande.Lignes.Sum(l => l.ConsigneUnitaire * l.Quantite);
        commande.ConsignesRendues = commande.Retours.Sum(r => r.Montant);

        db.Commandes.Add(commande);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return commande;
    }

    public async Task<List<Commande>> ListerCommandesAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Commandes
            .Where(c => c.EvenementId == evenementId)
            .Include(c => c.Lignes)
            .Include(c => c.Retours)
            .OrderByDescending(c => c.DateHeure).ThenByDescending(c => c.Id)
            .AsSplitQuery()
            .ToListAsync();
    }

    /// <summary>Annule une commande saisie par erreur : elle disparaît aussi des totaux.</summary>
    public async Task AnnulerCommandeAsync(int commandeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var evenementId = await db.Commandes
            .Where(c => c.Id == commandeId)
            .Select(c => (int?)c.EvenementId)
            .FirstOrDefaultAsync();

        await db.Commandes.Where(c => c.Id == commandeId).ExecuteDeleteAsync();
        if (evenementId is int id) notifications.Signaler(id);
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
            .Select(l => new { l.NomProduit, l.PrixUnitaire, l.Quantite, l.Commande!.Offerte })
            .ToListAsync();

        // Un même produit vendu à deux prix différents (tarif corrigé en cours de journée)
        // apparaît sur deux lignes distinctes, pour que les totaux restent vérifiables.
        var recap = lignes
            .GroupBy(l => (l.NomProduit, l.PrixUnitaire))
            .Select(g => new LigneRecap(
                g.Key.NomProduit,
                g.Key.PrixUnitaire,
                g.Where(l => !l.Offerte).Sum(l => l.Quantite),
                g.Where(l => !l.Offerte).Sum(l => l.PrixUnitaire * l.Quantite),
                g.Where(l => l.Offerte).Sum(l => l.Quantite),
                g.Where(l => l.Offerte).Sum(l => l.PrixUnitaire * l.Quantite)))
            .OrderByDescending(r => r.Montant)
            .ThenByDescending(r => r.QuantiteTotale)
            .ThenBy(r => r.Nom)
            .ToList();

        // Un passage où le client n'a fait que rendre ses verres n'est pas une vente :
        // le compter ferait baisser le panier moyen sans raison.
        var commandes = await db.Commandes
            .Where(c => c.EvenementId == evenementId && c.Lignes.Any())
            .Select(c => c.Offerte)
            .ToListAsync();

        var consignes = await MouvementsConsignesAsync(db, evenementId);

        return new Recapitulatif(
            evenement,
            recap,
            commandes.Count(offerte => !offerte),
            recap.Sum(r => r.Montant),
            commandes.Count(offerte => offerte),
            recap.Sum(r => r.MontantOffert),
            evenement.LieeAHelloAsso ? await ResumeHelloAssoAsync(db, evenementId) : null,
            consignes.Count > 0 || (evenement.Consigne > 0 && evenement.Produits.Any(p => p.Consigne))
                ? new ResumeConsignes(consignes)
                : null);
    }

    private static async Task<ResumeHelloAsso> ResumeHelloAssoAsync(BuvetteContext db, int evenementId)
    {
        var preCommandes = await db.PreCommandes
            .Where(p => p.EvenementId == evenementId && !p.Annulee)
            .Include(p => p.Lignes)
            .OrderBy(p => p.Nom).ThenBy(p => p.Prenom)
            .AsSplitQuery()
            .ToListAsync();

        var articles = preCommandes
            .SelectMany(p => p.Lignes.Select(l => (Ligne: l, p.Retiree)))
            .GroupBy(x => x.Ligne.NomArticle)
            .Select(g => new LigneHelloAsso(
                g.Key,
                g.Sum(x => x.Ligne.Quantite),
                g.Sum(x => x.Ligne.Montant),
                g.Where(x => x.Retiree).Sum(x => x.Ligne.Quantite)))
            .OrderByDescending(a => a.Montant).ThenBy(a => a.Nom)
            .ToList();

        return new ResumeHelloAsso(
            preCommandes.Count,
            preCommandes.Count(p => p.Retiree),
            preCommandes.Sum(p => p.MontantPaye),
            articles,
            preCommandes.Where(p => !p.Retiree).ToList());
    }

    /// <summary>Les pré-commandes d'un événement, par ordre alphabétique pour le comptoir.</summary>
    public async Task<List<PreCommande>> ListerPreCommandesAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.PreCommandes
            .Where(p => p.EvenementId == evenementId)
            .Include(p => p.Lignes)
            .OrderBy(p => p.Nom).ThenBy(p => p.Prenom).ThenBy(p => p.NumeroHelloAsso)
            .AsSplitQuery()
            .ToListAsync();
    }

    /// <summary>
    /// Coche ou décoche « retirée ». Décocher sert à corriger un clic de travers : les
    /// articles restent réservés dans les deux cas, seul le suivi du comptoir change.
    /// </summary>
    public async Task MarquerRetireeAsync(int preCommandeId, bool retiree)
    {
        await using var db = await factory.CreateDbContextAsync();
        var preCommande = await db.PreCommandes.FirstOrDefaultAsync(p => p.Id == preCommandeId)
            ?? throw new InvalidOperationException("Cette pré-commande n'existe plus : la liste a peut-être été resynchronisée.");

        if (retiree && preCommande.Annulee)
            throw new InvalidOperationException(
                $"La commande n° {preCommande.NumeroHelloAsso} a été annulée ou remboursée sur HelloAsso : rien à remettre.");

        // Une seconde caisse qui coche la même commande ne doit pas écraser l'heure du premier retrait.
        if (retiree == preCommande.Retiree) return;

        preCommande.RetireeLe = retiree ? DateTime.Now : null;
        await db.SaveChangesAsync();
        notifications.Signaler(preCommande.EvenementId);
    }
}
