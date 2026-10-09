using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Buvette.HelloAsso;

/// <summary>Erreur HelloAsso, avec un message lisible par un bénévole.</summary>
public class HelloAssoException(string message, Exception? interne = null) : Exception(message, interne);

/// <summary>
/// Lecture des campagnes et des commandes de l'association sur l'API HelloAsso v5.
/// Singleton : le jeton d'accès, valable une demi-heure, est réutilisé entre les appels.
/// </summary>
public class HelloAssoClient(IHttpClientFactory fabrique, IOptions<HelloAssoOptions> options, TimeProvider horloge)
{
    public const string NomClientHttp = "HelloAsso";
    private const int TaillePage = 100;

    private readonly SemaphoreSlim verrou = new(1, 1);
    private string? jeton;
    private DateTimeOffset expiration;

    public HelloAssoOptions Options => options.Value;

    public async Task<List<CampagneHelloAsso>> ListerCampagnesAsync(CancellationToken ct = default) =>
        await LirePagesAsync<CampagneHelloAsso>($"v5/organizations/{Organisation}/forms", ct);

    /// <summary>Toutes les commandes d'une campagne, avec leurs articles et leurs paiements.</summary>
    public async Task<List<CommandeHelloAsso>> ListerCommandesAsync(string typeFormulaire, string formulaire,
        CancellationToken ct = default) =>
        await LirePagesAsync<CommandeHelloAsso>(
            $"v5/organizations/{Organisation}/forms/{Uri.EscapeDataString(typeFormulaire)}/{Uri.EscapeDataString(formulaire)}/orders?withDetails=true",
            ct);

    /// <summary>Le détail d'une campagne, avec tous ses tarifs.</summary>
    public async Task<FormulaireHelloAsso> LireFormulaireAsync(string typeFormulaire, string formulaire,
        CancellationToken ct = default) =>
        await GetAsync<FormulaireHelloAsso>(
            $"v5/organizations/{Organisation}/forms/{Uri.EscapeDataString(typeFormulaire)}/{Uri.EscapeDataString(formulaire)}/public",
            ct)
        ?? throw new HelloAssoException("Réponse de HelloAsso illisible.");

    private string Organisation =>
        Uri.EscapeDataString(Options.Organisation?.Trim() ?? throw new HelloAssoException("Organisation HelloAsso non configurée."));

    /// <summary>L'API découpe les listes en pages ; on les suit jusqu'à la dernière.</summary>
    private async Task<List<T>> LirePagesAsync<T>(string chemin, CancellationToken ct)
    {
        var tout = new List<T>();
        string? suite = null;
        var separateur = chemin.Contains('?') ? '&' : '?';

        while (true)
        {
            var url = $"{chemin}{separateur}pageSize={TaillePage}"
                      + (suite is null ? "" : $"&continuationToken={Uri.EscapeDataString(suite)}");
            var page = await GetAsync<PageHelloAsso<T>>(url, ct);
            var donnees = page?.Data ?? [];
            tout.AddRange(donnees);

            suite = page?.Pagination?.ContinuationToken;
            if (donnees.Count < TaillePage || string.IsNullOrEmpty(suite)) return tout;
        }
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var http = CreerClient();
        using var requete = new HttpRequestMessage(HttpMethod.Get, url);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await JetonAsync(ct));

        using var reponse = await EnvoyerAsync(http, requete, ct);
        if (reponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Jeton révoqué entre-temps (clé régénérée sur HelloAsso) : on n'insiste pas.
            jeton = null;
            throw new HelloAssoException("HelloAsso refuse l'accès : la clé API a peut-être été régénérée.");
        }
        if (reponse.StatusCode == HttpStatusCode.NotFound)
            throw new HelloAssoException(
                $"HelloAsso ne trouve pas l'association « {Options.Organisation} » ou cette campagne. " +
                "Vérifiez l'identifiant dans l'adresse de la page HelloAsso de l'association.");
        if (reponse.StatusCode == HttpStatusCode.Forbidden)
            throw new HelloAssoException(
                $"Cette clé API ne donne pas accès à l'association « {Options.Organisation} ».");
        if (!reponse.IsSuccessStatusCode)
            throw new HelloAssoException($"HelloAsso a répondu par une erreur ({(int)reponse.StatusCode}). Réessayez plus tard.");

        return await reponse.Content.ReadFromJsonAsync<T>(ct);
    }

    private async Task<string> JetonAsync(CancellationToken ct)
    {
        await verrou.WaitAsync(ct);
        try
        {
            // Une minute de marge pour ne pas envoyer un jeton qui expire en route.
            if (jeton is not null && horloge.GetUtcNow() < expiration.AddMinutes(-1)) return jeton;

            if (!Options.EstConfiguree)
                throw new HelloAssoException("HelloAsso n'est pas configuré : identifiant, secret ou organisation manquant.");

            var http = CreerClient();
            using var requete = new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = Options.ClientId!.Trim(),
                    ["client_secret"] = Options.ClientSecret!.Trim(),
                }),
            };

            using var reponse = await EnvoyerAsync(http, requete, ct);
            if (reponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new HelloAssoException("HelloAsso refuse l'identifiant ou le secret de la clé API.");
            if (!reponse.IsSuccessStatusCode)
                throw new HelloAssoException($"HelloAsso a répondu par une erreur ({(int)reponse.StatusCode}). Réessayez plus tard.");

            var corps = await reponse.Content.ReadFromJsonAsync<ReponseJeton>(ct)
                        ?? throw new HelloAssoException("Réponse de HelloAsso illisible.");
            jeton = corps.access_token;
            expiration = horloge.GetUtcNow().AddSeconds(corps.expires_in);
            return jeton;
        }
        finally
        {
            verrou.Release();
        }
    }

    private HttpClient CreerClient()
    {
        var http = fabrique.CreateClient(NomClientHttp);
        http.BaseAddress ??= new Uri(Options.UrlApi.TrimEnd('/') + "/");
        return http;
    }

    private static async Task<HttpResponseMessage> EnvoyerAsync(HttpClient http, HttpRequestMessage requete, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(requete, ct);
        }
        catch (HttpRequestException e)
        {
            throw new HelloAssoException("Impossible de joindre HelloAsso : vérifiez la connexion Internet.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new HelloAssoException("HelloAsso ne répond pas. Réessayez dans un instant.", e);
        }
    }

    // Noms imposés par la norme OAuth2.
    private record ReponseJeton(string access_token, int expires_in);
}
