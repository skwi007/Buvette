using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Buvette.Components;
using Buvette.Data;
using Buvette.HelloAsso;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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

// La clé API HelloAsso reste hors du dépôt, dans les secrets utilisateur du PC. ASP.NET ne les
// lit qu'en Development, or la buvette tourne en Production : on les ajoute explicitement.
if (!builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.Configure<HelloAssoOptions>(builder.Configuration.GetSection(HelloAssoOptions.Section));
builder.Services.AddHttpClient(HelloAssoClient.NomClientHttp, http => http.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HelloAssoClient>();
builder.Services.AddScoped<SynchroHelloAsso>();

// Singleton : c'est lui qui relie entre eux les téléphones connectés en même temps.
builder.Services.AddSingleton<NotificationsBuvette>();
builder.Services.AddScoped<BuvetteService>();

// Mot de passe facultatif : défini seulement quand la buvette est exposée sur Internet
// (variable d'environnement Buvette__MotDePasse sur la Freebox). Absent, rien ne change.
var motDePasse = builder.Configuration["Buvette:MotDePasse"];
var protegee = !string.IsNullOrWhiteSpace(motDePasse);
if (protegee)
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/connexion";
            options.Cookie.Name = "buvette";
            // Un mois : on se connecte une fois sur la tablette, pas à chaque démonstration.
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
        });
    // Toute page, y compris la connexion temps réel de Blazor et l'export CSV, exige d'être connecté.
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    // Clés de chiffrement du cookie conservées à côté de la base : sans elles, chaque
    // redémarrage du serveur déconnecterait toutes les tablettes.
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "cles")));
    // Singleton : les essais de toutes les connexions doivent être comptés ensemble.
    builder.Services.AddSingleton<EssaisDeConnexion>();
}

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

// Sur la Freebox, Caddy reçoit le HTTPS et transmet en HTTP local : sans ces en-têtes,
// l'application se croirait en HTTP. Seul un proxy sur la machine elle-même est écouté.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

if (protegee)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseAntiforgery();

// Styles, logo et scripts restent publics : la page de connexion en a besoin.
app.MapStaticAssets().AllowAnonymous();

if (protegee)
{
    app.MapGet("/connexion", (string? returnUrl, string? erreur, int? attente) =>
            Results.Content(PageConnexion(returnUrl, erreur is not null, attente), "text/html; charset=utf-8"))
        .AllowAnonymous();

    app.MapPost("/connexion", async (HttpContext http, EssaisDeConnexion essais) =>
    {
        var formulaire = await http.Request.ReadFormAsync();
        var retour = AccesProtege.RetourSur(formulaire["returnUrl"]);

        // Derrière Caddy, l'adresse est celle du visiteur grâce à UseForwardedHeaders.
        var issue = essais.Essayer(http.Connection.RemoteIpAddress, formulaire["motDePasse"], motDePasse!);
        if (!issue.Accepte)
        {
            // Une seconde de pénalité freine les essais un par un ; EssaisDeConnexion arrête
            // ceux qui arrivent en nombre ou en parallèle.
            await Task.Delay(TimeSpan.FromSeconds(1));
            var motif = issue.Bloque ? $"attente={(int)Math.Ceiling(issue.Attente.TotalMinutes)}" : "erreur=1";
            return Results.Redirect($"/connexion?{motif}&returnUrl={Uri.EscapeDataString(retour)}");
        }

        var identite = new ClaimsIdentity([new Claim(ClaimTypes.Name, "benevole")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(new ClaimsPrincipal(identite), new AuthenticationProperties { IsPersistent = true });
        return Results.Redirect(retour);
    }).AllowAnonymous().DisableAntiforgery();

    app.MapGet("/deconnexion", async (HttpContext http) =>
    {
        await http.SignOutAsync();
        return Results.Redirect("/connexion");
    });
}
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

    if (recap.HelloAsso is { } helloAsso)
    {
        csv.AppendLine();
        csv.AppendLine("Preventes HelloAsso;Quantite prepayee;Quantite remise;Montant");
        foreach (var article in helloAsso.Articles)
            csv.AppendLine($"{Echapper(article.Nom)};{article.Quantite};{article.QuantiteRetiree};{article.Montant:0.00}");
        csv.AppendLine($"Commandes HelloAsso;{helloAsso.NombreCommandes};{helloAsso.NombreRetirees};{helloAsso.Total:0.00}");
        csv.AppendLine($"Recette totale (caisse + HelloAsso);;;{recap.RecetteTotale:0.00}");

        if (helloAsso.NonRetirees.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("Commandes HelloAsso non retirees;Nom;Articles;Montant");
            foreach (var p in helloAsso.NonRetirees)
                csv.AppendLine($"{p.NumeroHelloAsso};{Echapper($"{p.Nom} {p.Prenom}")};" +
                               $"{Echapper(string.Join(", ", p.Lignes.Select(l => $"{l.Quantite} x {l.NomArticle}")))};{p.MontantPaye:0.00}");
        }
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

// Page volontairement hors Blazor : elle doit s'afficher avant toute connexion temps réel.
static string PageConnexion(string? retour, bool erreur, int? attente)
{
    var champRetour = WebUtility.HtmlEncode(AccesProtege.RetourSur(retour));
    var message = attente is int minutes and > 0
        ? $"""<div class="alert alert-warning">Trop d'essais : réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.</div>"""
        : erreur
            ? """<div class="alert alert-danger">Mot de passe incorrect.</div>"""
            : "";
    return $$"""
        <!DOCTYPE html>
        <html lang="fr">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Connexion – Buvette</title>
            <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css" />
            <link rel="icon" type="image/png" href="/logo-ptitscoeurs.png" />
        </head>
        <body class="d-flex align-items-center justify-content-center" style="min-height:100vh">
            <form method="post" action="/connexion" class="card p-4 shadow-sm" style="width:100%;max-width:22rem">
                <img src="/logo-ptitscoeurs.png" alt="Les P'tits Cœurs de Rexa" class="mx-auto mb-3" style="max-height:5rem" />
                <h1 class="h4 text-center mb-3">Buvette</h1>
                {{message}}
                <input type="hidden" name="returnUrl" value="{{champRetour}}" />
                <label for="motDePasse" class="form-label">Mot de passe</label>
                <input id="motDePasse" name="motDePasse" type="password" class="form-control form-control-lg mb-3"
                       autocomplete="current-password" autofocus required />
                <button type="submit" class="btn btn-primary btn-lg w-100">Entrer</button>
            </form>
        </body>
        </html>
        """;
}

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
