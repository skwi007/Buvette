using System.Net;
using System.Text;
using Buvette.HelloAsso;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Buvette.Tests;

public class HelloAssoClientTests
{
    /// <summary>Faux serveur HelloAsso : répond selon le chemin et note chaque appel.</summary>
    private sealed class FauxHelloAsso(Func<HttpRequestMessage, HttpResponseMessage> repondre) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Appels { get; } = [];

        /// <summary>Corps des requêtes, lus à la réception : le client les libère ensuite.</summary>
        public List<string> Corps { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken ct)
        {
            Appels.Add(requete);
            Corps.Add(requete.Content is null ? "" : await requete.Content.ReadAsStringAsync(ct));
            return repondre(requete);
        }
    }

    private sealed class Fabrique(HttpMessageHandler serveur) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(serveur, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Jeton() =>
        Json("""{"access_token":"jeton-1","expires_in":1800,"token_type":"bearer"}""");

    private static HelloAssoClient Client(FauxHelloAsso serveur, TimeProvider? horloge = null) =>
        new(new Fabrique(serveur),
            Options.Create(new HelloAssoOptions { ClientId = "id", ClientSecret = "secret", Organisation = "ptits-coeurs" }),
            horloge ?? TimeProvider.System);

    private static bool EstJeton(HttpRequestMessage r) => r.RequestUri!.AbsolutePath == "/oauth2/token";

    [Fact]
    public async Task Le_jeton_est_demande_une_fois_puis_reutilise()
    {
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("""{"data":[]}"""));
        var client = Client(serveur);

        await client.ListerCampagnesAsync();
        await client.ListerCampagnesAsync();

        Assert.Single(serveur.Appels, EstJeton);
        Assert.All(serveur.Appels.Where(r => !EstJeton(r)),
            r => Assert.Equal("Bearer jeton-1", r.Headers.Authorization!.ToString()));
    }

    [Fact]
    public async Task Un_jeton_expire_est_redemande()
    {
        var horloge = new FakeTimeProvider();
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("""{"data":[]}"""));
        var client = Client(serveur, horloge);

        await client.ListerCampagnesAsync();
        horloge.Advance(TimeSpan.FromMinutes(29.5)); // dans la dernière minute de validité
        await client.ListerCampagnesAsync();

        Assert.Equal(2, serveur.Appels.Count(EstJeton));
    }

    [Fact]
    public async Task Le_jeton_est_demande_avec_les_identifiants_de_la_cle()
    {
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("""{"data":[]}"""));

        await Client(serveur).ListerCampagnesAsync();

        var corps = serveur.Corps[serveur.Appels.FindIndex(EstJeton)];
        Assert.Equal("grant_type=client_credentials&client_id=id&client_secret=secret", corps);
    }

    [Fact]
    public async Task Les_commandes_sont_lues_sur_toutes_les_pages()
    {
        // Une première page pleine (100 commandes), puis une seconde partielle.
        var page1 = string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"id":{{i}},"date":"2026-10-01T10:00:00+02:00"}"""));
        var serveur = new FauxHelloAsso(r =>
        {
            if (EstJeton(r)) return Jeton();
            return r.RequestUri!.Query.Contains("continuationToken=suite")
                ? Json("""{"data":[{"id":101,"date":"2026-10-01T10:00:00+02:00"}],"pagination":{"continuationToken":"fin"}}""")
                : Json($$$"""{"data":[{{{page1}}}],"pagination":{"continuationToken":"suite"}}""");
        });

        var commandes = await Client(serveur).ListerCommandesAsync("Shop", "preventes-marche-de-noel");

        Assert.Equal(101, commandes.Count);
        Assert.Equal(Enumerable.Range(1, 101).Select(i => (long)i), commandes.Select(c => c.Id));
        Assert.All(serveur.Appels.Where(r => !EstJeton(r)), r =>
            Assert.StartsWith("/v5/organizations/ptits-coeurs/forms/Shop/preventes-marche-de-noel/orders", r.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task Les_montants_en_centimes_sont_convertis_en_euros()
    {
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("""
            {"data":[{"id":42,"date":"2026-10-01T10:00:00+02:00",
              "payer":{"firstName":"Marie","lastName":"Muller"},
              "amount":{"total":1750},
              "items":[{"id":1,"name":"Tarte flambée","amount":850,"state":"Processed"},
                       {"id":2,"name":"Tarte flambée","amount":900,"state":"Processed"}]}]}
            """));

        var commande = Assert.Single(await Client(serveur).ListerCommandesAsync("Shop", "x"));

        Assert.Equal(17.50m, commande.Total);
        Assert.Equal([8.50m, 9.00m], commande.Items!.Select(a => a.Montant));
        Assert.Equal("Muller", commande.Payer!.LastName);
    }

    [Fact]
    public async Task Un_don_sans_nom_s_affiche_comme_un_don_et_les_espaces_en_trop_disparaissent()
    {
        // Forme réelle d'une commande Saint-Martin : le don n'a ni nom ni tarif.
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("""
            {"data":[{"id":196312977,"date":"2026-10-22T12:54:00+02:00",
              "amount":{"total":1100},
              "items":[{"id":1,"name":"Tarte flambée nature ","amount":900,"type":"Registration","tierId":102,"state":"Processed"},
                       {"id":2,"amount":200,"type":"Donation","priceCategory":"Fixed","state":"Processed"}]}]}
            """));

        var commande = Assert.Single(await Client(serveur).ListerCommandesAsync("Event", "saint-martin"));

        Assert.Equal(["Tarte flambée nature", "Don"], commande.Items!.Select(a => a.Libelle));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "identifiant ou le secret")]
    [InlineData(HttpStatusCode.Unauthorized, "identifiant ou le secret")]
    public async Task Une_cle_refusee_donne_un_message_clair(HttpStatusCode code, string attendu)
    {
        var serveur = new FauxHelloAsso(_ => Json("""{"error":"unauthorized_client"}""", code));

        var e = await Assert.ThrowsAsync<HelloAssoException>(() => Client(serveur).ListerCampagnesAsync());

        Assert.Contains(attendu, e.Message);
    }

    [Fact]
    public async Task Une_organisation_inconnue_donne_un_message_clair()
    {
        var serveur = new FauxHelloAsso(r => EstJeton(r) ? Jeton() : Json("{}", HttpStatusCode.NotFound));

        var e = await Assert.ThrowsAsync<HelloAssoException>(() => Client(serveur).ListerCampagnesAsync());

        Assert.Contains("ptits-coeurs", e.Message);
    }

    [Fact]
    public async Task Sans_internet_le_message_le_dit()
    {
        var serveur = new FauxHelloAsso(_ => throw new HttpRequestException("pas de réseau"));

        var e = await Assert.ThrowsAsync<HelloAssoException>(() => Client(serveur).ListerCampagnesAsync());

        Assert.Contains("connexion Internet", e.Message);
    }

    [Fact]
    public async Task Sans_configuration_aucun_appel_n_est_fait()
    {
        var serveur = new FauxHelloAsso(_ => Jeton());
        var client = new HelloAssoClient(new Fabrique(serveur),
            Options.Create(new HelloAssoOptions { ClientId = "id", Organisation = "ptits-coeurs" }), TimeProvider.System);

        await Assert.ThrowsAsync<HelloAssoException>(() => client.ListerCampagnesAsync());
        Assert.Empty(serveur.Appels);
    }
}
