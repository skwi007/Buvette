using Buvette.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

/// <summary>
/// Plusieurs bénévoles encaissent le dernier article au même instant, depuis deux ou trois
/// tablettes. On vérifie l'invariant qui compte : on ne vend jamais plus qu'on ne possède,
/// on ne laisse pas dormir du stock disponible, et aucune erreur technique n'atteint la caisse.
///
/// Ces tests utilisent un fichier SQLite réel, et non la base « :memory: » partagée des
/// autres tests : celle-ci passe par une connexion unique, ce qui rend toute concurrence
/// véritable impossible à reproduire.
///
/// Les effectifs testés (4 puis 8 caisses) correspondent au maximum plausible pour une
/// buvette. Au-delà d'une quarantaine d'encaissements simultanés — situation qui ne se
/// présentera pas ici — SQLite remonte des erreurs que l'application traite comme un échec
/// générique, sans jamais fausser les stocks pour autant.
/// </summary>
public sealed class ConcurrenceTests : IDisposable
{
    private readonly string _fichier = Path.Combine(Path.GetTempPath(), $"buvette-concurrence-{Guid.NewGuid():N}.db");
    private readonly BuvetteService _service;

    public ConcurrenceTests()
    {
        // WAL et délai d'attente : sans eux, SQLite renvoie « database is locked » dès que
        // deux écritures se croisent, ce qui masquerait le comportement que l'on veut observer.
        var chaine = new SqliteConnectionStringBuilder
        {
            DataSource = _fichier,
            DefaultTimeout = 30,
        }.ToString();

        using (var amorce = new SqliteConnection(chaine))
        {
            amorce.Open();
            using var pragma = amorce.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<BuvetteContext>().UseSqlite(chaine).Options;
        var fabrique = new Fabricant(options);

        using (var db = fabrique.CreateDbContext())
            db.Database.Migrate();

        _service = new BuvetteService(fabrique);
    }

    [Theory]
    [InlineData(1, 4)]  // le dernier article, quatre caisses
    [InlineData(3, 8)]
    public async Task Deux_caisses_simultanees_ne_peuvent_pas_vendre_plus_que_le_stock(int stock, int caisses)
    {
        var evenementId = await _service.CreerEvenementAsync(
            new Evenement { Nom = "Fête de l'école", Date = new DateOnly(2026, 9, 26) });
        await _service.AjouterProduitAsync(new Produit
        {
            EvenementId = evenementId,
            Nom = "Bretzel",
            Prix = 1m,
            StockInitial = stock,
        });
        var bretzel = (await _service.ProduitsActifsAsync(evenementId)).Single();

        // Toutes les caisses partent au même instant avec la même vision du stock.
        var depart = new TaskCompletionSource();
        var tentatives = Enumerable.Range(0, caisses).Select(async _ =>
        {
            await depart.Task;
            try
            {
                await _service.EncaisserAsync(evenementId, new Dictionary<int, int> { [bretzel.Id] = 1 });
                return (Vendu: true, Erreur: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Vendu: false, Erreur: ex);
            }
        }).ToList();

        depart.SetResult();
        var resultats = await Task.WhenAll(tentatives);

        // Aucune erreur technique ne doit atteindre le bénévole : seul « épuisé » est acceptable.
        var technique = resultats.Select(r => r.Erreur).OfType<SqliteException>().FirstOrDefault();
        Assert.True(technique is null, $"Erreur SQLite remontée à la caisse : {technique?.Message}");
        Assert.All(resultats.Where(r => !r.Vendu), r => Assert.IsType<StockInsuffisantException>(r.Erreur));

        var vendus = (await _service.RecapitulatifAsync(evenementId))!
            .Produits.SingleOrDefault()?.Quantite ?? 0;

        // Ni survente, ni sous-vente : tout le stock disponible part, et pas un de plus.
        Assert.Equal(Math.Min(stock, caisses), vendus);

        // Les encaissements réussis correspondent exactement aux ventes enregistrées :
        // aucune caisse ne doit croire avoir encaissé une commande qui n'existe pas.
        Assert.Equal(resultats.Count(r => r.Vendu), vendus);

        // Le stock affiché aux bénévoles reste cohérent avec ce qui a été réellement vendu.
        var restant = (await _service.ProduitsEnVenteAsync(evenementId)).Single().StockRestant;
        Assert.Equal(stock - vendus, restant);
    }

    [Fact]
    public async Task Une_commande_refusee_pour_concurrence_ne_laisse_aucune_trace()
    {
        var evenementId = await _service.CreerEvenementAsync(
            new Evenement { Nom = "Fête de l'école", Date = new DateOnly(2026, 9, 26) });
        await _service.AjouterProduitAsync(new Produit
        {
            EvenementId = evenementId, Nom = "Bretzel", Prix = 1m, StockInitial = 1,
        });
        var bretzel = (await _service.ProduitsActifsAsync(evenementId)).Single();

        var depart = new TaskCompletionSource();
        var tentatives = Enumerable.Range(0, 6).Select(async _ =>
        {
            await depart.Task;
            try { await _service.EncaisserAsync(evenementId, new Dictionary<int, int> { [bretzel.Id] = 1 }); return true; }
            catch (Exception) { return false; }
        }).ToList();

        depart.SetResult();
        await Task.WhenAll(tentatives);

        var commandes = await _service.ListerCommandesAsync(evenementId);
        var recap = await _service.RecapitulatifAsync(evenementId);

        // Une transaction annulée ne doit laisser ni commande orpheline ni ligne fantôme.
        Assert.Single(commandes);
        Assert.Single(commandes[0].Lignes);
        Assert.Equal(1m, recap!.TotalVentes);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _fichier, _fichier + "-wal", _fichier + "-shm" })
            try { File.Delete(f); } catch (IOException) { /* nettoyage au mieux */ }
    }

    private sealed class Fabricant(DbContextOptions<BuvetteContext> options) : IDbContextFactory<BuvetteContext>
    {
        public BuvetteContext CreateDbContext() => new(options);
    }
}
