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

| Produit | Prix | Stock |
| --- | --- | --- |
| Tarte flambée nature | 8,00 € | 40 |
| Tarte flambée gratinée | 9,00 € | 30 |
| Jus de pomme | 1,50 € | *(vide)* |
| Bouteille d'eau | 1,00 € | *(vide)* |
| Bretzel | 1,00 € | 50 |

Les flèches ↑ ↓ règlent l'ordre des boutons sur l'écran de caisse. L'interrupteur
*En vente* retire un produit épuisé sans toucher aux ventes déjà faites.

**La colonne *Stock* est facultative.** Laissée vide, le produit est illimité — c'est le bon
choix pour des bouteilles réapprovisionnées au fil de l'eau. Renseignée, elle indique la
quantité prévue pour la journée, et la colonne *Restant* montre en temps réel ce qu'il
reste. Modifier ce nombre en cours de journée revient à réapprovisionner : passer les
bretzels de 50 à 80 remet aussitôt 30 articles en vente.

Le restant n'est jamais stocké, il se déduit des ventes. Annuler une commande saisie par
erreur remet donc automatiquement les articles à disposition.

### 2. Encaisser

La page **Caisse** affiche un grand bouton par produit. Chaque appui ajoute une unité ;
le panier à droite se met à jour avec le total à payer. Un champ *Montant reçu* calcule
la monnaie à rendre. **Encaisser** enregistre la commande et remet le panier à zéro.

Les produits dont le stock est suivi affichent ce qu'il en reste — *Plus que 7* — en orange
sous les cinq derniers, puis **ÉPUISÉ** en rouge. Un produit épuisé n'est plus cliquable,
et le compte tient aussi de ce qui est déjà dans le panier : impossible d'en mettre huit
alors qu'il n'en reste que sept.

Si une autre caisse écoule le dernier article entre l'ouverture de l'écran et la validation,
l'encaissement est refusé avec un message clair, et rien n'est enregistré.

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

116 tests xUnit couvrent les règles métier, dans `tests/Buvette.Tests/`.
Ils tournent sur un vrai moteur SQLite, recréé pour chaque test : les cascades de
suppression, les migrations et le stockage des montants sont donc réellement exercés,
pas simulés. Les tests de concurrence utilisent un fichier temporaire, seule façon de
faire se croiser plusieurs caisses pour de bon.

Ils couvrent en priorité les points où une erreur coûterait de l'argent à l'association :

- le total recalculé depuis les prix en base, jamais depuis ce qu'affiche l'écran ;
- les prix figés au moment de la vente, insensibles aux corrections ultérieures ;
- l'exactitude au centime, sans dérive de virgule flottante ;
- le refus d'encaisser sur une buvette clôturée ou au-delà du stock ;
- l'absence totale d'écriture quand un encaissement est refusé ;
- l'impossibilité de vendre plus que le stock, même depuis plusieurs caisses à la fois.

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
| `src/Buvette/Data/Migrations/` | Migrations EF Core du schéma |
| `src/Buvette/Components/Pages/Home.razor` | Liste et création des événements |
| `src/Buvette/Components/Pages/EvenementPage.razor` | Réglages et carte des produits |
| `src/Buvette/Components/Pages/Caisse.razor` | Écran de vente |
| `src/Buvette/Components/Pages/Historique.razor` | Récapitulatif et détail des commandes |
| `tests/Buvette.Tests/` | Tests xUnit des règles métier |

## Faire évoluer le schéma

La base est gérée par les **migrations EF Core**, appliquées automatiquement au démarrage.
Une évolution du modèle s'applique donc à une base contenant déjà les ventes d'un
événement, sans rien perdre.

Après avoir modifié une classe de `Data/Modeles.cs` :

```console
dotnet tool restore
dotnet ef migrations add NomDeLaMigration --project src/Buvette --output-dir Data/Migrations
```

La migration est appliquée au prochain lancement. Les tests s'exécutent eux aussi sur une
base créée par migration : une migration oubliée les fait échouer immédiatement.
