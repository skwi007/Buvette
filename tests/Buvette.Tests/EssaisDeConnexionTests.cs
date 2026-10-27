using System.Collections.Concurrent;
using System.Net;
using Buvette.Data;
using Microsoft.Extensions.Time.Testing;

namespace Buvette.Tests;

public class EssaisDeConnexionTests
{
    private static readonly IPAddress Robot = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress Benevole = IPAddress.Parse("198.51.100.20");

    private readonly FakeTimeProvider horloge = new();
    private readonly EssaisDeConnexion essais;

    public EssaisDeConnexionTests() => essais = new EssaisDeConnexion(horloge);

    private IssueEssai Essayer(IPAddress adresse, string saisie = "mauvais") => essais.Essayer(adresse, saisie, "tarte");

    /// <summary>Des erreurs ordinaires : refusées, mais pas encore bloquées.</summary>
    private void Rater(IPAddress adresse, int fois)
    {
        for (var i = 0; i < fois; i++)
            Assert.Equal(new IssueEssai(false, TimeSpan.Zero), Essayer(adresse));
    }

    [Fact]
    public void Apres_trop_d_erreurs_meme_le_bon_mot_de_passe_est_bloque()
    {
        Rater(Robot, EssaisDeConnexion.ErreursParAdresse);

        var issue = Essayer(Robot, "tarte");

        Assert.False(issue.Accepte);
        Assert.Equal(EssaisDeConnexion.Fenetre, issue.Attente);
    }

    [Fact]
    public void Cent_essais_simultanes_ne_donnent_que_les_essais_permis()
    {
        var issues = new ConcurrentBag<IssueEssai>();

        Parallel.For(0, 100, _ => issues.Add(Essayer(Robot)));

        Assert.Equal(EssaisDeConnexion.ErreursParAdresse, issues.Count(i => !i.Bloque));
    }

    [Fact]
    public void Le_blocage_d_une_adresse_ne_gene_pas_les_autres()
    {
        Rater(Robot, EssaisDeConnexion.ErreursParAdresse);

        Assert.True(Essayer(Benevole, "tarte").Accepte);
    }

    [Fact]
    public void L_attente_diminue_puis_l_adresse_peut_reessayer()
    {
        Rater(Robot, EssaisDeConnexion.ErreursParAdresse);

        horloge.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(10), Essayer(Robot).Attente);

        horloge.Advance(TimeSpan.FromMinutes(10));
        Assert.True(Essayer(Robot, "tarte").Accepte);
    }

    [Fact]
    public void Une_connexion_reussie_efface_les_erreurs_de_son_adresse()
    {
        Rater(Benevole, EssaisDeConnexion.ErreursParAdresse - 1);
        Assert.True(Essayer(Benevole, "Tarte").Accepte);

        // Sans remise à zéro, cette seconde série serait bloquée dès son deuxième essai.
        Rater(Benevole, EssaisDeConnexion.ErreursParAdresse);
    }

    [Fact]
    public void Un_robot_qui_change_d_adresse_est_arrete_par_le_plafond_commun()
    {
        for (var i = 0; i < EssaisDeConnexion.ErreursAuTotal; i++)
            Rater(IPAddress.Parse($"203.0.113.{i}"), 1);

        var issue = Essayer(Benevole, "tarte");

        Assert.False(issue.Accepte);
        Assert.True(issue.Bloque);
    }
}
