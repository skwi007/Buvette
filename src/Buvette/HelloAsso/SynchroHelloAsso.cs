using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.HelloAsso;

/// <summary>Bilan d'une synchronisation, à afficher au bénévole qui l'a lancée.</summary>
public record ResultatSynchro(int PreCommandes, int Nouvelles, int NouvellesAnnulees, int ProduitsCrees);

/// <summary>
/// Recopie une campagne HelloAsso dans une buvette : ses tarifs deviennent des produits, ses
/// commandes des pré-commandes à remettre au comptoir. On peut resynchroniser autant de fois
/// que l'on veut : rien n'est créé en double, et ce que les bénévoles ont saisi (prix,
/// stock, commandes retirées) est conservé.
/// </summary>
public class SynchroHelloAsso(
    HelloAssoClient helloAsso, IDbContextFactory<BuvetteContext> factory, NotificationsBuvette notifications)
{
    /// <summary>Crée une buvette à partir d'une campagne et y importe tout de suite ses commandes.</summary>
    public async Task<int> CreerBuvetteAsync(CampagneHelloAsso campagne, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var dejaLiee = await db.Evenements
            .Where(e => e.HelloAssoType == campagne.FormType && e.HelloAssoCampagne == campagne.FormSlug)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);
        if (dejaLiee is int id) return id;

        var evenement = new Evenement
        {
            Nom = Tronquer(campagne.Title?.Trim() is { Length: > 0 } titre ? titre : campagne.FormSlug, 120),
            Date = campagne.StartDate is { } debut
                ? DateOnly.FromDateTime(debut.ToLocalTime().Date)
                : DateOnly.FromDateTime(DateTime.Today),
            HelloAssoType = campagne.FormType,
            HelloAssoCampagne = campagne.FormSlug,
        };
        db.Evenements.Add(evenement);
        await db.SaveChangesAsync(ct);

        await SynchroniserAsync(evenement.Id, ct);
        return evenement.Id;
    }

    /// <summary>Relie une buvette existante à une campagne, puis importe ses commandes.</summary>
    public async Task<ResultatSynchro> LierAsync(int evenementId, CampagneHelloAsso campagne, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await db.Evenements
                .Where(e => e.Id == evenementId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.HelloAssoType, campagne.FormType)
                    .SetProperty(e => e.HelloAssoCampagne, campagne.FormSlug), ct);
        }
        return await SynchroniserAsync(evenementId, ct);
    }

    /// <summary>
    /// Détache la buvette de sa campagne. Les pré-commandes disparaissent avec le lien : elles
    /// n'auraient plus de source pour être tenues à jour. Les produits restent, vendables au
    /// comptoir comme les autres.
    /// </summary>
    public async Task DelierAsync(int evenementId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.PreCommandes.Where(p => p.EvenementId == evenementId).ExecuteDeleteAsync();
        await db.Produits
            .Where(p => p.EvenementId == evenementId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.TarifHelloAssoId, (long?)null));
        await db.Evenements
            .Where(e => e.Id == evenementId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.HelloAssoType, (string?)null)
                .SetProperty(e => e.HelloAssoCampagne, (string?)null)
                .SetProperty(e => e.HelloAssoSynchroLe, (DateTime?)null));

        await transaction.CommitAsync();
        notifications.Signaler(evenementId);
    }

    /// <summary>Relit la campagne liée sur HelloAsso et met la buvette à jour.</summary>
    public async Task<ResultatSynchro> SynchroniserAsync(int evenementId, CancellationToken ct = default)
    {
        string type, campagne;
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var evenement = await db.Evenements.FirstOrDefaultAsync(e => e.Id == evenementId, ct)
                ?? throw new InvalidOperationException("Cet événement n'existe plus.");
            if (!evenement.LieeAHelloAsso)
                throw new InvalidOperationException("Cette buvette n'est liée à aucune campagne HelloAsso.");
            (type, campagne) = (evenement.HelloAssoType!, evenement.HelloAssoCampagne!);
        }

        var formulaire = await helloAsso.LireFormulaireAsync(type, campagne, ct);
        var commandes = await helloAsso.ListerCommandesAsync(type, campagne, ct);
        return await AppliquerAsync(evenementId, formulaire.Tiers ?? [], commandes, ct);
    }

    /// <summary>
    /// Le cœur de la synchronisation, séparé des appels réseau pour être testé tel quel.
    /// </summary>
    public async Task<ResultatSynchro> AppliquerAsync(int evenementId,
        IReadOnlyList<TarifHelloAsso> tarifs, IReadOnlyList<CommandeHelloAsso> commandes, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var evenement = await db.Evenements.FirstOrDefaultAsync(e => e.Id == evenementId, ct)
            ?? throw new InvalidOperationException("Cet événement n'existe plus.");

        var produits = new CorrespondanceProduits(db, evenementId,
            await db.Produits.Where(p => p.EvenementId == evenementId).ToListAsync(ct));

        // Tous les tarifs deviennent des produits, même ceux que personne n'a encore achetés
        // en ligne : ils se vendront peut-être au comptoir.
        foreach (var tarif in tarifs.Where(t => t.TierType is not "Donation" && !string.IsNullOrWhiteSpace(t.Label)))
            produits.Pour(tarif.Id, tarif.Label!, tarif.Prix);

        var connues = await db.PreCommandes
            .Include(p => p.Lignes)
            .Where(p => p.EvenementId == evenementId)
            .ToDictionaryAsync(p => p.NumeroHelloAsso, ct);

        int total = 0, nouvelles = 0, nouvellesAnnulees = 0;

        foreach (var commande in commandes)
        {
            var articles = (commande.Items ?? []).Where(a => !a.EstUnDon).ToList();
            // Un simple don n'a rien à remettre au comptoir.
            if (articles.Count == 0) continue;

            var annulee = commande.EstAnnulee;
            // Une commande annulée garde ses articles à l'écran, pour que le bénévole comprenne
            // ce qui a été remboursé ; une commande partiellement annulée ne garde que le reste.
            if (!annulee) articles = articles.Where(a => !a.EstAnnule).ToList();

            var lignes = articles
                .GroupBy(a => (a.TierId, Nom: (a.Name ?? "").Trim()))
                .Select(g => new LignePreCommande
                {
                    Produit = g.Key.TierId is long tarif ? produits.Pour(tarif, g.Key.Nom, g.First().Montant) : null,
                    NomArticle = Tronquer(g.Key.Nom.Length > 0 ? g.Key.Nom : "(article sans nom)", 200),
                    Quantite = g.Count(),
                    Montant = g.Sum(a => a.Montant + (a.Options ?? []).Sum(o => o.Amount) / 100m),
                })
                .ToList();

            if (!connues.TryGetValue(commande.Id, out var preCommande))
            {
                preCommande = new PreCommande { EvenementId = evenementId, NumeroHelloAsso = commande.Id };
                db.PreCommandes.Add(preCommande);
                nouvelles++;
            }
            else
            {
                if (annulee && !preCommande.Annulee) nouvellesAnnulees++;
                db.LignesPreCommandes.RemoveRange(preCommande.Lignes);
            }

            preCommande.Nom = Tronquer(commande.Payer?.LastName?.Trim() ?? "", 120);
            preCommande.Prenom = Tronquer(commande.Payer?.FirstName?.Trim() ?? "", 120);
            preCommande.DateCommande = commande.Date.ToLocalTime().DateTime;
            preCommande.Annulee = annulee;
            preCommande.MontantPaye = annulee ? 0m : lignes.Sum(l => l.Montant);
            preCommande.Lignes = lignes;
            total++;
        }

        evenement.HelloAssoSynchroLe = DateTime.Now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        notifications.Signaler(evenementId);

        return new ResultatSynchro(total, nouvelles, nouvellesAnnulees, produits.Crees);
    }

    private static string Tronquer(string texte, int longueur) =>
        texte.Length <= longueur ? texte : texte[..longueur];

    /// <summary>Retrouve, ou crée, le produit de la buvette qui correspond à un tarif HelloAsso.</summary>
    private sealed class CorrespondanceProduits(BuvetteContext db, int evenementId, List<Produit> produits)
    {
        private readonly Dictionary<long, Produit> parTarif = produits
            .Where(p => p.TarifHelloAssoId is not null)
            .ToDictionary(p => p.TarifHelloAssoId!.Value);

        private int dernierOrdre = produits.Count == 0 ? 0 : produits.Max(p => p.Ordre);

        public int Crees { get; private set; }

        public Produit Pour(long tarif, string libelle, decimal prix)
        {
            if (parTarif.TryGetValue(tarif, out var connu)) return connu;

            var nom = Tronquer(libelle.Trim(), 120);

            // Un produit de même nom, repris d'une buvette précédente ou saisi à la main, est
            // le même article : on le relie plutôt que d'afficher deux boutons identiques.
            var produit = produits.FirstOrDefault(p =>
                p.TarifHelloAssoId is null && string.Equals(p.Nom.Trim(), nom, StringComparison.OrdinalIgnoreCase));

            if (produit is null)
            {
                produit = new Produit { EvenementId = evenementId, Nom = nom, Prix = prix, Ordre = ++dernierOrdre };
                db.Produits.Add(produit);
                produits.Add(produit);
                Crees++;
            }

            produit.TarifHelloAssoId = tarif;
            parTarif[tarif] = produit;
            return produit;
        }
    }
}
