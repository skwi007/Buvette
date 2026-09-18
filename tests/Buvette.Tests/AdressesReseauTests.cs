using Buvette.Data;

namespace Buvette.Tests;

public class AdressesReseauTests
{
    private static CarteReseau Carte(
        string nom, string description, string adresse,
        bool active = true, bool avecPasserelle = true) =>
        new(nom, description, active, avecPasserelle, adresse);

    /// <summary>
    /// Les cartes réellement présentes sur le poste de développement : un PC d'entreprise
    /// avec VPN Palo Alto, Hyper-V et une station d'accueil USB. Seule la carte USB, reliée
    /// à la box du domicile, est joignable depuis un téléphone.
    /// </summary>
    private static CarteReseau[] PosteReel() =>
    [
        Carte("vEthernet (nat)", "Hyper-V Virtual Ethernet Adapter #3", "172.18.144.1"),
        Carte("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter #2", "172.21.48.1"),
        Carte("vEthernet (36bc00e4)", "Hyper-V Virtual Ethernet Adapter", "172.18.80.1"),
        Carte("Ethernet 5", "PANGP Virtual Ethernet Adapter Secure #2", "10.231.239.64"),
        Carte("Wi-Fi 5", "Intel(R) Wi-Fi 7 BE201 320MHz", "169.254.216.115", avecPasserelle: false),
        Carte("Ethernet 3", "Realtek USB GbE Family Controller #2", "192.168.1.50"),
        Carte("Wi-Fi 3", "Intel(R) Wi-Fi 7 BE201 320MHz", "169.254.79.111", avecPasserelle: false),
        Carte("Wi-Fi", "Intel(R) Wi-Fi 7 BE201 320MHz", "10.243.37.58", active: false),
    ];

    [Fact]
    public void Sur_un_poste_d_entreprise_seule_la_carte_du_reseau_local_est_proposee()
    {
        var joignables = AdressesReseau.Joignables(PosteReel());

        var carte = Assert.Single(joignables);
        Assert.Equal("192.168.1.50", carte.Adresse);
        Assert.Equal("Ethernet 3", carte.Nom);
    }

    [Fact]
    public void Le_vpn_d_entreprise_n_est_jamais_propose()
    {
        // Il répond depuis le poste, ce qui le rend trompeur : aucun téléphone ne l'atteint.
        var joignables = AdressesReseau.Joignables(PosteReel());

        Assert.DoesNotContain(joignables, c => c.Adresse == "10.231.239.64");
    }

    [Fact]
    public void Les_commutateurs_virtuels_ne_sont_jamais_proposes()
    {
        var joignables = AdressesReseau.Joignables(PosteReel());

        Assert.DoesNotContain(joignables, c => c.Adresse.StartsWith("172."));
    }

    [Theory]
    [InlineData("Hyper-V Virtual Ethernet Adapter")]
    [InlineData("PANGP Virtual Ethernet Adapter Secure")]
    [InlineData("VMware Virtual Ethernet Adapter")]
    [InlineData("VirtualBox Host-Only Ethernet Adapter")]
    [InlineData("TAP-Windows Adapter V9")]
    [InlineData("Docker Host Network Driver")]
    [InlineData("Teredo Tunneling Pseudo-Interface")]
    public void Une_carte_virtuelle_est_ecartee_quel_que_soit_son_editeur(string description)
    {
        var joignables = AdressesReseau.Joignables([Carte("X", description, "192.168.1.50")]);

        Assert.Empty(joignables);
    }

    [Fact]
    public void Une_carte_sans_adresse_obtenue_est_ecartee()
    {
        // 169.254.x.x signale l'échec du DHCP : la carte n'est reliée à rien d'utilisable.
        var joignables = AdressesReseau.Joignables(
            [Carte("Wi-Fi 5", "Intel(R) Wi-Fi 7", "169.254.216.115", avecPasserelle: false)]);

        Assert.Empty(joignables);
    }

    [Fact]
    public void Une_carte_debranchee_est_ecartee_meme_avec_une_adresse_valide()
    {
        var joignables = AdressesReseau.Joignables(
            [Carte("Wi-Fi", "Intel(R) Wi-Fi 7", "10.243.37.58", active: false)]);

        Assert.Empty(joignables);
    }

    [Fact]
    public void Une_carte_sans_passerelle_est_ecartee()
    {
        var joignables = AdressesReseau.Joignables(
            [Carte("Ethernet 9", "Realtek USB GbE", "192.168.5.2", avecPasserelle: false)]);

        Assert.Empty(joignables);
    }

    [Fact]
    public void La_boucle_locale_n_est_pas_proposee_comme_adresse_reseau()
    {
        var joignables = AdressesReseau.Joignables([Carte("Loopback", "Software Loopback", "127.0.0.1")]);

        Assert.Empty(joignables);
    }

    [Fact]
    public void Le_wifi_est_propose_quand_il_est_reellement_connecte()
    {
        var joignables = AdressesReseau.Joignables(
            [Carte("Wi-Fi", "Intel(R) Wi-Fi 7 BE201 320MHz", "192.168.1.42")]);

        Assert.Equal("192.168.1.42", Assert.Single(joignables).Adresse);
    }

    [Fact]
    public void Plusieurs_cartes_valides_sont_toutes_proposees_et_classees_par_nom()
    {
        var joignables = AdressesReseau.Joignables(
        [
            Carte("Wi-Fi", "Intel(R) Wi-Fi 7", "192.168.1.42"),
            Carte("Ethernet 3", "Realtek USB GbE", "192.168.1.50"),
        ]);

        Assert.Equal(["Ethernet 3", "Wi-Fi"], joignables.Select(c => c.Nom));
    }

    [Fact]
    public void Un_poste_sans_reseau_ne_propose_aucune_adresse()
    {
        Assert.Empty(AdressesReseau.Joignables([]));
    }
}
