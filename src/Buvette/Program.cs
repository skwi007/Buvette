using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Buvette.Components;
using Buvette.Data;
using Microsoft.EntityFrameworkCore;

// Prix en euros, dates et virgules décimales : toute l'application parle français.
var culture = new CultureInfo("fr-FR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// La buvette tourne en environnement Production depuis le dossier du projet, pas depuis une
// publication. Sans cet appel, ASP.NET ne sait pas retrouver blazor.web.js ni les styles :
// les pages s'affichent mais aucun bouton ne répond.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// La base vit à côté de l'exécutable : un simple copier-coller du fichier suffit à la sauvegarder.
var cheminBase = Path.Combine(builder.Environment.ContentRootPath, "buvette.db");
builder.Services.AddDbContextFactory<BuvetteContext>(options =>
    options.UseSqlite($"Data Source={cheminBase}"));

builder.Services.AddScoped<BuvetteService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Migrations plutôt que EnsureCreated : une évolution du modèle doit pouvoir s'appliquer
    // à une base contenant déjà les ventes d'un événement, sans la recréer.
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BuvetteContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Export du récapitulatif pour le trésorier de l'association.
app.MapGet("/export/{id:int}", async (int id, BuvetteService service) =>
{
    var recap = await service.RecapitulatifAsync(id);
    if (recap is null) return Results.NotFound();

    var csv = new StringBuilder();
    csv.AppendLine("Produit;Prix unitaire;Quantite vendue;Montant;Quantite offerte;Valeur offerte;Sorti du stock");
    foreach (var ligne in recap.Produits)
        csv.AppendLine($"{Echapper(ligne.Nom)};{ligne.PrixUnitaire:0.00};{ligne.Quantite};{ligne.Montant:0.00};" +
                       $"{ligne.QuantiteOfferte};{ligne.MontantOffert:0.00};{ligne.QuantiteTotale}");

    csv.AppendLine();
    csv.AppendLine($"Nombre de commandes encaissees;;;{recap.NombreCommandes}");
    csv.AppendLine($"Total des ventes;;;{recap.TotalVentes:0.00}");
    csv.AppendLine($"Commandes offertes;;;{recap.NombreOffertes}");
    csv.AppendLine($"Valeur offerte (hors recette);;;{recap.TotalOffert:0.00}");
    csv.AppendLine($"Fond de caisse;;;{recap.Evenement.FondDeCaisse:0.00}");
    csv.AppendLine($"Total attendu en caisse;;;{recap.TotalEnCaisse:0.00}");

    if (recap.Evenement.MontantCompte is decimal compte)
    {
        csv.AppendLine($"Montant compte;;;{compte:0.00}");
        csv.AppendLine($"Ecart;;;{recap.Ecart!.Value:0.00}");
    }

    // BOM UTF-8 : sans lui, Excel affiche « tarte flambée » en mojibake.
    var contenu = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    var nom = $"buvette-{Nettoyer(recap.Evenement.Nom)}-{recap.Evenement.Date:yyyy-MM-dd}.csv";
    return Results.File(contenu, "text/csv", nom);

    static string Echapper(string valeur) => valeur.Replace(';', ',');
    static string Nettoyer(string valeur) =>
        string.Concat(valeur.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')).Trim('-');
});

AfficherAdressesReseau(app);

app.Run();

// Les bénévoles prennent les commandes sur tablette : l'adresse à taper doit être visible au lancement.
static void AfficherAdressesReseau(WebApplication app)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var adresses = app.Urls.ToList();
        var port = adresses
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Port : 0)
            .FirstOrDefault(p => p > 0);
        if (port == 0) return;

        var cartes = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => new CarteReseau(
                    Nom: n.Name,
                    Description: n.Description,
                    Active: n.OperationalStatus == OperationalStatus.Up,
                    AvecPasserelle: n.GetIPProperties().GatewayAddresses
                        .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                               && !g.Address.Equals(IPAddress.Any)),
                    Adresse: a.Address.ToString())));

        var joignables = AdressesReseau.Joignables(cartes);

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Buvette");
        logger.LogInformation("Buvette prête sur ce poste : http://localhost:{Port}", port);

        if (joignables.Count == 0)
        {
            logger.LogWarning(
                "Aucun réseau local détecté : les tablettes ne pourront pas se connecter. " +
                "Branchez le poste au même réseau (Wi-Fi ou câble) que les tablettes.");
            return;
        }

        logger.LogInformation("Depuis une tablette ou un téléphone du même réseau :");
        foreach (var carte in joignables)
            logger.LogInformation("    http://{Ip}:{Port}   (via « {Carte} »)", carte.Adresse, port, carte.Nom);

        logger.LogInformation(
            "Si la page ne s'ouvre pas sur la tablette, autorisez le port {Port} dans le pare-feu Windows " +
            "(voir la section « Depuis une tablette » du README).", port);
    });
}
