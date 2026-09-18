# Buvette

Gestion de la buvette d'une association : préparer la carte d'un événement, encaisser
les commandes en un clic le jour J, et sortir le récapitulatif des ventes en fin de journée.

Application ASP.NET Core 10 / Blazor Server, base SQLite locale, aucune connexion Internet requise.

## Lancer l'application

```console
dotnet run --project src/Buvette
```

Puis ouvrir <http://localhost:5000>.

## Depuis une tablette ou un téléphone

Plusieurs personnes peuvent encaisser en parallèle sur le même événement. Au démarrage,
la console affiche l'adresse à taper, par exemple :

```text
Depuis une tablette ou un téléphone du même réseau :
    http://192.168.1.50:5000   (via « Ethernet 3 »)
```

Seules les adresses réellement joignables sont proposées : les commutateurs Hyper-V,
Docker et le VPN d'entreprise sont volontairement masqués, car ils répondent depuis le
poste mais restent inaccessibles à une tablette.

**Si la page ne s'ouvre pas sur la tablette**, dans l'ordre :

1. **La tablette est-elle sur le même réseau ?** Même box, pas de 4G, pas de réseau
   « invité » (beaucoup de box isolent les appareils entre eux sur le réseau invité).
2. **Le pare-feu Windows bloque le port.** C'est la cause la plus fréquente. Ouvrir
   PowerShell **en tant qu'administrateur** et lancer une fois pour toutes :

   ```powershell
   New-NetFirewallRule -DisplayName "Buvette (port 5000)" -Direction Inbound `
       -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private
   ```

   Pour retirer l'autorisation plus tard :

   ```powershell
   Remove-NetFirewallRule -DisplayName "Buvette (port 5000)"
   ```

3. **Le poste est-il connecté par le bon réseau ?** Si le Wi-Fi du PC est déconnecté et
   qu'il passe par un câble ou une station d'accueil, c'est l'adresse de cette carte qu'il
   faut utiliser — celle qu'affiche la console, pas celle du VPN.

Pour la journée de l'événement, `Lancer-buvette.cmd` démarre l'application et ouvre le
navigateur sans passer par la ligne de commande. Fermer la fenêtre noire arrête la buvette.

### Sur un poste sans SDK .NET

Publier une fois depuis un poste équipé, puis copier le dossier obtenu :

```console
dotnet publish src/Buvette -c Release -r win-x64 --self-contained -o publie
```

`publie\Buvette.exe` se lance alors par un double-clic, sans rien installer.

## Utilisation

### 1. Créer l'événement et sa carte

Depuis l'accueil, **Nouvel événement** : nom, date et *fond de caisse* (la monnaie déposée
dans la caisse avant l'ouverture). Si un événement existe déjà, sa liste de produits peut
être reprise telle quelle — pratique d'une année sur l'autre.

Sur la page **Produits**, ajouter chaque article avec son prix :

| Produit | Prix |
| --- | --- |
| Tarte flambée nature | 8,00 € |
| Tarte flambée gratinée | 9,00 € |
| Jus de pomme | 1,50 € |
| Bouteille d'eau | 1,00 € |
| Bretzel | 1,00 € |

Les flèches ↑ ↓ règlent l'ordre des boutons sur l'écran de caisse. L'interrupteur
*En vente* retire un produit épuisé sans toucher aux ventes déjà faites.

### 2. Encaisser

La page **Caisse** affiche un grand bouton par produit. Chaque appui ajoute une unité ;
le panier à droite se met à jour avec le total à payer. Un champ *Montant reçu* calcule
la monnaie à rendre. **Encaisser** enregistre la commande et remet le panier à zéro.

### 3. Récapituler

La page **Historique** donne, pour chaque produit, la quantité vendue et le montant, puis :

- le **total des ventes**,
- le **montant attendu dans la caisse** (fond de caisse + ventes), à comparer au comptage réel.

Le détail commande par commande permet d'annuler une saisie erronée. Le récapitulatif
s'imprime, ou s'exporte en CSV pour Excel (séparateur `;`, décimales à la virgule).

## Points de fonctionnement à connaître

- **Les prix sont figés à la vente.** Chaque ligne de commande recopie le nom et le prix
  du produit. Corriger un prix ou supprimer un produit en cours de journée ne modifie
  jamais les ventes déjà encaissées ; un produit vendu à deux prix apparaît sur deux
  lignes du récapitulatif.
- **Clôturer** une buvette bloque toute nouvelle vente sans rien effacer. L'historique
  reste consultable et la buvette peut être rouverte.
- **Annuler une commande la supprime définitivement** des totaux : c'est le seul moyen
  de corriger une erreur de caisse.

## Sauvegarde

Toutes les données tiennent dans le fichier `buvette.db`, sous `src/Buvette/` (ou à côté
de l'exécutable après publication). Le copier suffit à sauvegarder l'ensemble des
événements ; le remettre en place suffit à les restaurer. À faire de préférence
application arrêtée.

Ce fichier est volontairement exclu du dépôt Git (`.gitignore`) : les ventes de
l'association n'ont pas à être publiées avec le code.

## Tests

```console
dotnet test
```

91 tests xUnit couvrent les règles métier, dans `tests/Buvette.Tests/`.
Ils tournent sur une vraie base SQLite en mémoire, recréée pour chaque test : les cascades
de suppression et le stockage des montants sont donc réellement exercés, pas simulés.

Ils vérifient notamment les points où une erreur coûterait de l'argent à l'association :
le total recalculé depuis les prix en base et non depuis l'écran, les prix figés au moment
de la vente, l'exactitude au centime, le refus d'encaisser sur une buvette clôturée, et le
fait qu'un encaissement refusé n'écrit rien en base.

## Structure

| Chemin | Rôle |
| --- | --- |
| `Buvette.slnx` | Solution regroupant l'application et ses tests |
| `Lancer-buvette.cmd` | Démarrage en un double-clic le jour de l'événement |
| `src/Buvette/Program.cs` | Démarrage, base de données, export CSV |
| `src/Buvette/Data/Modeles.cs` | Événement, produit, commande, ligne de commande |
| `src/Buvette/Data/BuvetteContext.cs` | Configuration EF Core et relations |
| `src/Buvette/Data/BuvetteService.cs` | Règles métier : encaissement, récapitulatif |
| `src/Buvette/Data/Prix.cs` | Lecture des montants saisis (virgule ou point) |
| `src/Buvette/Data/AdressesReseau.cs` | Tri des adresses réseau proposées aux tablettes |
| `src/Buvette/Components/Pages/Home.razor` | Liste et création des événements |
| `src/Buvette/Components/Pages/EvenementPage.razor` | Réglages et carte des produits |
| `src/Buvette/Components/Pages/Caisse.razor` | Écran de vente |
| `src/Buvette/Components/Pages/Historique.razor` | Récapitulatif et détail des commandes |
| `tests/Buvette.Tests/` | Tests xUnit des règles métier |

Le schéma est créé automatiquement au premier démarrage (`EnsureCreated`). Toute
modification du modèle après une mise en production demandera de passer aux migrations
EF Core pour ne pas perdre les données existantes.
