using Buvette.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

/// <summary>
/// Une base SQLite en mémoire, neuve pour chaque test. C'est bien le moteur SQLite réel qui
/// tourne — et non un fournisseur en mémoire d'EF — pour que les tests couvrent aussi les
/// cascades de suppression et le stockage des décimaux, là où les erreurs se cachent.
/// La connexion reste ouverte le temps du test : SQLite efface une base « :memory: » dès
/// que sa dernière connexion se ferme.
/// </summary>
public sealed class BaseDeTest : IDisposable
{
    private readonly SqliteConnection _connexion;

    public IDbContextFactory<BuvetteContext> Fabrique { get; }
    public BuvetteService Service { get; }

    public BaseDeTest()
    {
        _connexion = new SqliteConnection("Data Source=:memory:");
        _connexion.Open();

        var options = new DbContextOptionsBuilder<BuvetteContext>().UseSqlite(_connexion).Options;
        Fabrique = new Fabricant(options);

        using (var db = Fabrique.CreateDbContext())
            db.Database.EnsureCreated();

        Service = new BuvetteService(Fabrique);
    }

    /// <summary>Accès direct à la base, pour vérifier ce que le service a réellement écrit.</summary>
    public BuvetteContext Contexte() => Fabrique.CreateDbContext();

    /// <summary>La carte de l'énoncé : fête de l'école du 26 septembre 2026.</summary>
    public async Task<CarteDeTest> FeteDeLEcoleAsync(decimal fondDeCaisse = 50m)
    {
        var evenementId = await Service.CreerEvenementAsync(new Evenement
        {
            Nom = "Fête de l'école",
            Date = new DateOnly(2026, 9, 26),
            FondDeCaisse = fondDeCaisse,
        });

        var tarifs = new (string Nom, decimal Prix)[]
        {
            ("Tarte flambée nature", 8m),
            ("Tarte flambée gratinée", 9m),
            ("Jus de pomme", 1.5m),
            ("Bouteille d'eau", 1m),
            ("Bretzel", 1m),
        };

        foreach (var (nom, prix) in tarifs)
            await Service.AjouterProduitAsync(new Produit { EvenementId = evenementId, Nom = nom, Prix = prix });

        var produits = await Service.ProduitsActifsAsync(evenementId);
        return new CarteDeTest(evenementId, produits.ToDictionary(p => p.Nom, p => p.Id));
    }

    public void Dispose() => _connexion.Dispose();

    private sealed class Fabricant(DbContextOptions<BuvetteContext> options)
        : IDbContextFactory<BuvetteContext>
    {
        public BuvetteContext CreateDbContext() => new(options);
    }
}

/// <summary>Un événement de test et les identifiants de ses produits, repérés par leur nom.</summary>
public sealed record CarteDeTest(int EvenementId, IReadOnlyDictionary<string, int> Produits)
{
    public int this[string nom] => Produits[nom];

    /// <summary>Raccourci pour composer un panier à partir des noms de produits.</summary>
    public Dictionary<int, int> Panier(params (string Nom, int Quantite)[] lignes) =>
        lignes.ToDictionary(l => Produits[l.Nom], l => l.Quantite);
}

/// <summary>
/// xUnit construit une instance de classe de test par test : chaque test part donc
/// d'une base vierge, sans qu'il faille la réinitialiser explicitement.
/// </summary>
public abstract class TestAvecBase : IDisposable
{
    protected readonly BaseDeTest Base = new();
    protected BuvetteService Service => Base.Service;

    public void Dispose()
    {
        Base.Dispose();
        GC.SuppressFinalize(this);
    }
}
