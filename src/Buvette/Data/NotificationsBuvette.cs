namespace Buvette.Data;

/// <summary>
/// Prévient tous les écrans ouverts qu'un événement vient de changer (vente, annulation,
/// produit modifié, clôture...). Une seule instance pour toute l'application : chaque
/// téléphone est un circuit Blazor distinct, mais tous tournent dans le même processus,
/// si bien qu'un simple événement .NET suffit à les relier.
/// </summary>
/// <remarks>
/// Le signal ne transporte que l'identifiant de l'événement : chaque écran relit ensuite
/// la base. Aucune donnée n'est donc diffusée qui pourrait diverger de ce qui est enregistré.
/// </remarks>
public class NotificationsBuvette(ILogger<NotificationsBuvette>? journal = null)
{
    public event Action<int>? EvenementModifie;

    public void Signaler(int evenementId)
    {
        if (EvenementModifie is not { } abonnes) return;

        // Chaque abonné est protégé des autres : l'écran d'un téléphone déconnecté ne doit
        // ni empêcher les autres d'être prévenus, ni faire échouer la vente qui a déclenché
        // le signal alors qu'elle est déjà enregistrée.
        foreach (var abonne in abonnes.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                abonne(evenementId);
            }
            catch (Exception ex)
            {
                journal?.LogWarning(ex, "Un écran n'a pas pu être prévenu du changement de l'événement {Evenement}", evenementId);
            }
        }
    }
}
