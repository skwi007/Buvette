using Buvette.Data;

namespace Buvette.Tests;

public class AccesProtegeTests
{
    [Theory]
    [InlineData("tarte")]
    [InlineData("Tarte")]          // majuscule ajoutée d'office par le clavier de la tablette
    [InlineData("  TARTE ")]
    public void Le_mot_de_passe_est_accepte_sans_tenir_compte_de_la_casse_ni_des_espaces(string saisie) =>
        Assert.True(AccesProtege.Verifier(saisie, "tarte"));

    [Theory]
    [InlineData("tartes")]
    [InlineData("tart")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Un_mauvais_mot_de_passe_est_refuse(string? saisie) =>
        Assert.False(AccesProtege.Verifier(saisie, "tarte"));

    [Fact]
    public void Un_mot_de_passe_attendu_vide_ne_laisse_entrer_personne() =>
        Assert.False(AccesProtege.Verifier("", ""));

    [Theory]
    [InlineData("/caisse/3", "/caisse/3")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://site-pirate.example", "/")]
    [InlineData("//site-pirate.example", "/")]
    [InlineData("/\\site-pirate.example", "/")]
    public void Apres_connexion_on_ne_revient_que_dans_l_application(string? retour, string attendu) =>
        Assert.Equal(attendu, AccesProtege.RetourSur(retour));
}
