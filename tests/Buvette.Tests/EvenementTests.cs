using Buvette.Data;
using Microsoft.EntityFrameworkCore;

namespace Buvette.Tests;

public class EvenementTests : TestAvecBase
{
    [Fact]
    public async Task Creer_enregistre_les_informations_de_l_evenement()
    {
        var id = await Service.CreerEvenementAsync(new Evenement
        {
            Nom = "Fête de l'école",
            Date = new DateOnly(2026, 9, 26),
            FondDeCaisse = 50m,
        });

        var evenement = await Service.ChargerEvenementAsync(id);

        Assert.NotNull(evenement);
        Assert.Equal("Fête de l'école", evenement.Nom);
        Assert.Equal(new DateOnly(2026, 9, 26), evenement.Date);
        Assert.Equal(50m, evenement.FondDeCaisse);
        Assert.False(evenement.Cloture);
    }

    [Fact]
    public async Task Charger_un_evenement_inconnu_renvoie_null()
    {
        Assert.Null(await Service.ChargerEvenementAsync(404));
    }

    [Fact]
    public async Task Lister_classe_les_evenements_du_plus_recent_au_plus_ancien()
    {
        await Service.CreerEvenementAsync(new Evenement { Nom = "Marché de Noël", Date = new DateOnly(2026, 12, 5) });
        await Service.CreerEvenementAsync(new Evenement { Nom = "Fête de l'école", Date = new DateOnly(2026, 9, 26) });
        await Service.CreerEvenementAsync(new Evenement { Nom = "Tournoi", Date = new DateOnly(2027, 3, 14) });

        var liste = await Service.ListerEvenementsAsync();

        Assert.Equal(["Tournoi", "Marché de Noël", "Fête de l'école"], liste.Select(e => e.Nom));
    }

    [Fact]
    public async Task Creer_en_reprenant_un_evenement_precedent_recopie_la_carte()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        // Un produit épuisé l'an dernier doit rester épuisé dans la copie.
        var bretzel = (await Service.ProduitsActifsAsync(carte.EvenementId)).Single(p => p.Nom == "Bretzel");
        bretzel.Actif = false;
        await Service.EnregistrerProduitAsync(bretzel);

        var nouveau = await Service.CreerEvenementAsync(
            new Evenement { Nom = "Fête de l'école 2027", Date = new DateOnly(2027, 9, 25) },
            copierProduitsDepuis: carte.EvenementId);

        var copie = await Service.ChargerEvenementAsync(nouveau);
        var produits = copie!.Produits.OrderBy(p => p.Ordre).ToList();

        Assert.Equal(5, produits.Count);
        Assert.Equal("Tarte flambée nature", produits[0].Nom);
        Assert.Equal(8m, produits[0].Prix);
        Assert.False(produits.Single(p => p.Nom == "Bretzel").Actif);
    }

    [Fact]
    public async Task Creer_en_reprenant_un_evenement_precedent_ne_recopie_pas_ses_ventes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3)));

        var nouveau = await Service.CreerEvenementAsync(
            new Evenement { Nom = "Fête de l'école 2027", Date = new DateOnly(2027, 9, 25) },
            copierProduitsDepuis: carte.EvenementId);

        var recap = await Service.RecapitulatifAsync(nouveau);

        Assert.Empty(recap!.Produits);
        Assert.Equal(0m, recap.TotalVentes);
        Assert.Equal(0, recap.NombreCommandes);
    }

    [Fact]
    public async Task Creer_sans_modele_part_d_une_carte_vide()
    {
        var id = await Service.CreerEvenementAsync(new Evenement { Nom = "Vide", Date = new DateOnly(2026, 1, 1) });

        Assert.Empty(await Service.ProduitsActifsAsync(id));
    }

    [Fact]
    public async Task Enregistrer_met_a_jour_l_evenement_sans_toucher_a_ses_produits()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        var evenement = await Service.ChargerEvenementAsync(carte.EvenementId);

        evenement!.Nom = "Fête de l'école — édition 2026";
        evenement.FondDeCaisse = 80m;
        evenement.Cloture = true;
        await Service.EnregistrerEvenementAsync(evenement);

        var relu = await Service.ChargerEvenementAsync(carte.EvenementId);
        Assert.Equal("Fête de l'école — édition 2026", relu!.Nom);
        Assert.Equal(80m, relu.FondDeCaisse);
        Assert.True(relu.Cloture);
        Assert.Equal(5, relu.Produits.Count);
    }

    [Fact]
    public async Task Supprimer_un_evenement_efface_aussi_ses_produits_ses_commandes_et_leurs_lignes()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 3), ("Jus de pomme", 1)));

        await Service.SupprimerEvenementAsync(carte.EvenementId);

        await using var db = Base.Contexte();
        Assert.Null(await Service.ChargerEvenementAsync(carte.EvenementId));
        Assert.Equal(0, await db.Produits.CountAsync());
        Assert.Equal(0, await db.Commandes.CountAsync());
        // Sans cascade en base, des lignes orphelines s'accumuleraient silencieusement.
        Assert.Equal(0, await db.Lignes.CountAsync());
    }

    [Fact]
    public async Task Supprimer_un_evenement_laisse_les_autres_intacts()
    {
        var garde = await Base.FeteDeLEcoleAsync();
        var jete = await Base.FeteDeLEcoleAsync();
        await Service.EncaisserAsync(garde.EvenementId, garde.Panier(("Bretzel", 2)));

        await Service.SupprimerEvenementAsync(jete.EvenementId);

        var recap = await Service.RecapitulatifAsync(garde.EvenementId);
        Assert.Equal(2m, recap!.TotalVentes);
        Assert.Equal(5, (await Service.ProduitsActifsAsync(garde.EvenementId)).Count);
    }
}
