using Buvette.Data;
using Buvette.HelloAsso;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Buvette.E2E;

/// <summary>
/// La buvette lancée pour de vrai, sur un port Kestrel libre, et un Chromium pour la piloter.
/// La base vit dans un dossier temporaire : jamais celle du projet, qui contient les ventes
/// réelles de l'association. HelloAsso est vidé pour qu'aucun test n'appelle l'API, même si
/// la clé est présente dans les secrets utilisateur du poste.
/// </summary>
public class ApplicationBuvette : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "buvette-e2e-" + Guid.NewGuid().ToString("N"));
    private IPlaywright? _playwright;

    public IBrowser Navigateur { get; private set; } = default!;
    public string Adresse { get; private set; } = "";

    /// <summary>Mot de passe exigé par l'application ; <c>null</c> la laisse ouverte, comme sur le PC.</summary>
    protected virtual string? MotDePasse => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dossier);
        builder.UseContentRoot(_dossier);
        builder.UseEnvironment("Production");
        // Lu par Program.cs avant la construction de l'application : il faut un réglage d'hôte.
        builder.UseSetting("Buvette:MotDePasse", MotDePasse ?? "");
        // Les secrets utilisateur sont ajoutés après ces réglages et l'emporteraient :
        // on vide donc les options elles-mêmes, une fois toute la configuration lue.
        builder.ConfigureTestServices(services => services.PostConfigure<HelloAssoOptions>(options =>
        {
            options.ClientId = null;
            options.ClientSecret = null;
            options.Organisation = null;
        }));
    }

    public async Task InitializeAsync()
    {
        UseKestrel(0);
        StartServer();
        Adresse = ClientOptions.BaseAddress.ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        Navigateur = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Un onglet neuf, sans cookie ni historique, ouvert sur l'application.</summary>
    public async Task<IPage> NouvelOngletAsync()
    {
        var contexte = await Navigateur.NewContextAsync(new() { BaseURL = Adresse, Locale = "fr-FR" });
        return await contexte.NewPageAsync();
    }

    /// <summary>Le service métier de l'application, pour préparer une carte sans passer par l'écran.</summary>
    public async Task<T> AvecServiceAsync<T>(Func<BuvetteService, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<BuvetteService>());
    }

    /// <summary>Un événement prêt à encaisser, au nom unique pour ne pas croiser ceux des autres tests.</summary>
    public Task<int> EvenementAsync(string nom, decimal fondDeCaisse = 50m, params (string Nom, decimal Prix, int? Stock)[] produits) =>
        AvecServiceAsync(async service =>
        {
            var id = await service.CreerEvenementAsync(new Evenement
            {
                Nom = nom,
                Date = new DateOnly(2026, 9, 26),
                FondDeCaisse = fondDeCaisse,
            });
            foreach (var (nomProduit, prix, stock) in produits)
                await service.AjouterProduitAsync(new Produit
                {
                    EvenementId = id,
                    Nom = nomProduit,
                    Prix = prix,
                    StockInitial = stock,
                });
            return id;
        });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Navigateur.DisposeAsync();
        _playwright?.Dispose();
        await base.DisposeAsync();
        try { Directory.Delete(_dossier, recursive: true); }
        catch (IOException) { /* SQLite peut garder le fichier quelques instants : le dossier temporaire sera nettoyé plus tard. */ }
    }
}

/// <summary>La même application, protégée par mot de passe comme sur la Freebox.</summary>
public sealed class ApplicationProtegee : ApplicationBuvette
{
    public const string Secret = "tarte-flambee";

    protected override string? MotDePasse => Secret;
}
