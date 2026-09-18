using System.Globalization;
using Buvette.Data;

namespace Buvette.Tests;

public class PrixTests
{
    [Theory]
    [InlineData("8,50", "8.50")]   // virgule tapée au clavier français
    [InlineData("8.50", "8.50")]   // point renvoyé par un champ HTML de type number
    [InlineData("8", "8")]
    [InlineData("0", "0")]
    [InlineData("  1,5  ", "1.5")]
    [InlineData("1 000,50", "1000.50")]      // espace de séparation des milliers
    [InlineData("1 000,50", "1000.50")] // espace insécable
    [InlineData("1 000,50", "1000.50")] // espace fine insécable (Windows fr-FR)
    public void Analyser_lit_les_montants_saisis_a_la_main(string saisie, string attendu)
    {
        var valeur = decimal.Parse(attendu, CultureInfo.InvariantCulture);
        Assert.Equal(valeur, Prix.Analyser(saisie));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("8,5,5")]
    [InlineData("€")]
    public void Analyser_renvoie_null_sur_une_saisie_inexploitable(string? saisie)
    {
        Assert.Null(Prix.Analyser(saisie));
    }

    [Fact]
    public void Analyser_arrondit_au_centime()
    {
        Assert.Equal(8.57m, Prix.Analyser("8,566"));
        Assert.Equal(8.56m, Prix.Analyser("8,564"));
    }

    [Fact]
    public void Analyser_accepte_un_montant_negatif_et_laisse_l_appelant_le_refuser()
    {
        // Le refus est du ressort de la page, qui affiche un message ;
        // la lecture, elle, doit distinguer « -5 » d'une saisie illisible.
        Assert.Equal(-5m, Prix.Analyser("-5"));
    }

    [Theory]
    [InlineData("8.5", "8.5")]
    [InlineData("1.5", "1.5")]     // « 1,50 € » doit sortir en 1.5, sinon le champ s'affiche vide
    [InlineData("8", "8")]
    [InlineData("0", "0")]
    [InlineData("1234.56", "1234.56")]
    public void PourChamp_formate_toujours_avec_un_point_decimal(string valeur, string attendu)
    {
        // Un champ HTML type="number" rejette « 1,5 » : le navigateur vide alors la case,
        // et le bénévole voit un prix disparaître sans comprendre pourquoi.
        var montant = decimal.Parse(valeur, CultureInfo.InvariantCulture);

        Assert.Equal(attendu, Prix.PourChamp(montant));
        Assert.DoesNotContain(",", Prix.PourChamp(montant));
    }

    [Fact]
    public void PourChamp_rend_une_chaine_vide_pour_une_valeur_absente()
    {
        Assert.Equal("", Prix.PourChamp(null));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void PourChamp_ne_depend_pas_de_la_culture_du_poste(string culture)
    {
        var precedente = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1.5", Prix.PourChamp(1.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = precedente;
        }
    }

    [Fact]
    public void Un_montant_formate_pour_un_champ_se_relit_a_l_identique()
    {
        // La boucle complète : afficher un prix, le recevoir du navigateur, le relire.
        foreach (var montant in new[] { 0m, 1.5m, 8m, 9.99m, 1234.56m })
            Assert.Equal(montant, Prix.Analyser(Prix.PourChamp(montant)));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")] // culture invariante
    public void Analyser_donne_le_meme_resultat_quelle_que_soit_la_culture_du_poste(string culture)
    {
        // Un poste configuré en anglais ne doit pas lire « 8,50 » comme 850.
        var precedente = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(8.5m, Prix.Analyser("8,50"));
            Assert.Equal(8.5m, Prix.Analyser("8.50"));
        }
        finally
        {
            CultureInfo.CurrentCulture = precedente;
        }
    }
}
