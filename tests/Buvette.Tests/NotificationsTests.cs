using Buvette.Data;

namespace Buvette.Tests;

/// <summary>
/// Les autres caisses ne se mettent à jour que si le service signale chaque modification.
/// Un oubli ne casse rien de visible sur le téléphone qui a agi : ce sont les autres qui
/// affichent alors un stock périmé. D'où ces tests, un par type de modification.
/// </summary>
public class NotificationsTests : TestAvecBase
{
    private readonly List<int> _signaux = [];

    public NotificationsTests() => Base.Notifications.EvenementModifie += _signaux.Add;

    /// <summary>Oublie les signaux émis pendant la préparation, pour ne vérifier que l'action testée.</summary>
    private async Task<CarteDeTest> CarteAsync()
    {
        var carte = await Base.FeteDeLEcoleAsync();
        _signaux.Clear();
        return carte;
    }

    [Fact]
    public async Task Une_vente_previent_les_autres_caisses()
    {
        var carte = await CarteAsync();

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2)));

        Assert.Equal([carte.EvenementId], _signaux);
    }

    [Fact]
    public async Task Une_vente_offerte_previent_aussi_car_elle_consomme_du_stock()
    {
        var carte = await CarteAsync();

        await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)), offerte: true);

        Assert.Equal([carte.EvenementId], _signaux);
    }

    [Fact]
    public async Task Une_vente_refusee_ne_previent_personne()
    {
        var carte = await CarteAsync();
        await Base.DefinirStockAsync(carte, "Bretzel", 1);
        _signaux.Clear();

        await Assert.ThrowsAsync<StockInsuffisantException>(() =>
            Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 2))));

        Assert.Empty(_signaux);
    }

    [Fact]
    public async Task Annuler_une_commande_previent_car_le_stock_revient()
    {
        var carte = await CarteAsync();
        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));
        _signaux.Clear();

        await Service.AnnulerCommandeAsync(commande.Id);

        Assert.Equal([carte.EvenementId], _signaux);
    }

    [Fact]
    public async Task Annuler_une_commande_deja_supprimee_ne_previent_personne()
    {
        await CarteAsync();

        await Service.AnnulerCommandeAsync(424242);

        Assert.Empty(_signaux);
    }

    [Fact]
    public async Task Modifier_ajouter_deplacer_ou_supprimer_un_produit_previent()
    {
        var carte = await CarteAsync();
        var bretzel = carte.Produits["Bretzel"];

        await Base.DefinirStockAsync(carte, "Bretzel", 10);
        await Service.AjouterProduitAsync(new Produit { EvenementId = carte.EvenementId, Nom = "Café", Prix = 1m });
        await Service.DeplacerProduitAsync(bretzel, -1);
        await Service.SupprimerProduitAsync(bretzel);

        Assert.Equal(Enumerable.Repeat(carte.EvenementId, 4), _signaux);
    }

    [Fact]
    public async Task Cloturer_ou_compter_la_caisse_previent()
    {
        var carte = await CarteAsync();
        var evenement = (await Service.ChargerEvenementAsync(carte.EvenementId))!;

        evenement.Cloture = true;
        await Service.EnregistrerEvenementAsync(evenement);
        await Service.EnregistrerComptageAsync(carte.EvenementId, 50m);

        Assert.Equal([carte.EvenementId, carte.EvenementId], _signaux);
    }

    [Fact]
    public async Task Un_ecran_en_erreur_n_empeche_ni_la_vente_ni_les_autres_ecrans()
    {
        var carte = await CarteAsync();
        Base.Notifications.EvenementModifie += _ => throw new InvalidOperationException("écran déconnecté");
        var prevenusApres = new List<int>();
        Base.Notifications.EvenementModifie += prevenusApres.Add;

        var commande = await Service.EncaisserAsync(carte.EvenementId, carte.Panier(("Bretzel", 1)));

        Assert.True(commande.Id > 0);
        Assert.Equal([carte.EvenementId], _signaux);
        Assert.Equal([carte.EvenementId], prevenusApres);
    }
}
