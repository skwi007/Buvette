using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class ProduitTests : TestAvecBase
{
    [Fact]
    public async Task Ajouter_place_chaque_produit_a_la_suite_du_precedent()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        var produits = await Service.ProduitsActifsAsync(carte.EvenementId);

        Assert.Equal([1, 2, 3, 4, 5], produits.Select(p => p.Ordre));
        Assert.Equal("Tarte flambée nature", produits[0].Nom);
        Assert.Equal("Bretzel", produits[4].Nom);
    }

    [Fact]
    public async Task Ajouter_numerote_independamment_pour_chaque_evenement()
    {
        var premiere = await Base.FeteDeLEcoleAsync();
        var seconde = await Base.FeteDeLEcoleAsync();

        Assert.Equal([1, 2, 3, 4, 5], (await Service.ProduitsActifsAsync(premiere.EvenementId)).Select(p => p.Ordre));
        Assert.Equal([1, 2, 3, 4, 5], (await Service.ProduitsActifsAsync(seconde.EvenementId)).Select(p => p.Ordre));
    }

    [Fact]
    public async Task Un_produit_retire_de_la_vente_disparait_des_boutons_de_caisse()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var jus = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Jus de pomme");

        jus.Actif = false;
        await Service.EnregistrerProduitAsync(jus);

        var actifs = await Service.ProduitsActifsAsync(carte.EvenementId);
        Assert.Equal(4, actifs.Count);
        Assert.DoesNotContain(actifs, p => p.Nom == "Jus de pomme");
    }

    [Fact]
    public async Task Enregistrer_met_a_jour_le_nom_et_le_prix()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");

        bretzel.Nom = "Bretzel maison";
        bretzel.Prix = 1.2m;
        await Service.EnregistrerProduitAsync(bretzel);

        var relu = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Id == bretzel.Id);
        Assert.Equal("Bretzel maison", relu.Nom);
        Assert.Equal(1.2m, relu.Prix);
    }

    [Fact]
    public async Task Enregistrer_un_produit_ne_modifie_pas_ses_voisins()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var produits = await Service.ProduitsActifsAsync(carte.EvenementId);
        var bretzel = produits.Single(p => p.Nom == "Bretzel");

        bretzel.Prix = 1.2m;
        await Service.EnregistrerProduitAsync(bretzel);

        var apres = await Service.ProduitsActifsAsync(carte.EvenementId);
        Assert.Equal(8m, apres.Single(p => p.Nom == "Tarte flambée nature").Prix);
        Assert.Equal([1, 2, 3, 4, 5], apres.Select(p => p.Ordre));
    }

    [Fact]
    public async Task Deplacer_vers_le_haut_echange_le_produit_avec_le_precedent()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.DeplacerProduitAsync(carte["Bretzel"], -1);

        var noms = (await Service.ProduitsActifsAsync(carte.EvenementId)).Select(p => p.Nom);
        Assert.Equal(
            ["Tarte flambée nature", "Tarte flambée gratinée", "Jus de pomme", "Bretzel", "Bouteille d'eau"],
            noms);
    }

    [Fact]
    public async Task Deplacer_vers_le_bas_echange_le_produit_avec_le_suivant()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.DeplacerProduitAsync(carte["Tarte flambée nature"], 1);

        var noms = (await Service.ProduitsActifsAsync(carte.EvenementId)).Select(p => p.Nom);
        Assert.Equal(
            ["Tarte flambée gratinée", "Tarte flambée nature", "Jus de pomme", "Bouteille d'eau", "Bretzel"],
            noms);
    }

    [Fact]
    public async Task Deplacer_renumerote_les_positions_sans_trou()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.DeplacerProduitAsync(carte["Bretzel"], -1);

        Assert.Equal([1, 2, 3, 4, 5], (await Service.ProduitsActifsAsync(carte.EvenementId)).Select(p => p.Ordre));
    }

    [Theory]
    [InlineData("Tarte flambée nature", -1)] // déjà en tête
    [InlineData("Bretzel", 1)]               // déjà en queue
    public async Task Deplacer_au_dela_des_bords_ne_change_rien(string nom, int sens)
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var avant = (await Service.ProduitsActifsAsync(carte.EvenementId)).Select(p => p.Nom).ToList();

        await Service.DeplacerProduitAsync(carte[nom], sens);

        Assert.Equal(avant, (await Service.ProduitsActifsAsync(carte.EvenementId)).Select(p => p.Nom));
    }

    [Fact]
    public async Task Deplacer_un_produit_inconnu_ne_leve_pas_d_erreur()
    {
        var carte = await Base.FeteDeLEcoleAsync();

        await Service.DeplacerProduitAsync(404, -1);

        Assert.Equal(5, (await Service.ProduitsActifsAsync(carte.EvenementId)).Count);
    }

    [Fact]
    public async Task Supprimer_un_produit_conserve_les_ventes_deja_encaissees()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Jus de pomme", 4)));

        await Service.SupprimerProduitAsync(carte["Jus de pomme"]);

        var recap = await Service.RecapitulatifAsync(carte.EvenementId);
        var ligne = recap!.Produits.Single(p => p.Nom == "Jus de pomme");
        Assert.Equal(4, ligne.Quantite);
        Assert.Equal(6m, ligne.Montant);
        Assert.Equal(6m, recap.TotalVentes);
    }

    [Fact]
    public async Task Supprimer_un_produit_detache_la_ligne_sans_l_effacer()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Jus de pomme", 4)));

        await Service.SupprimerProduitAsync(carte["Jus de pomme"]);

        await using var db = Base.Contexte();
        var ligne = await db.Lignes.SingleAsync();
        Assert.Null(ligne.ProduitId);
        Assert.Equal("Jus de pomme", ligne.NomProduit);
        Assert.Equal(1.5m, ligne.PrixUnitaire);
    }
}
