# Plan : pré-commandes payées sur HelloAsso

Statut : réalisé le 09/10/2026 par l'API HelloAsso, sans import de fichier. Mode d'emploi : [helloasso.md](helloasso.md). Trois écarts au plan ci-dessous :

- retirer une pré-commande ne crée pas de `Commande`, et le booléen `Offerte` reste en place : les préventes sont comptées à part dans la recette ;
- les articles prépayés sont déduits du stock dès la synchronisation, qu'ils soient retirés ou non ;
- un article HelloAsso correspond à un seul produit : les formules ne sont pas encore décomposées.

## Objectif

Une partie des commandes est payée en avance sur HelloAsso. Le jour de la buvette, il faut :

- une liste des pré-commandes avec le **numéro de commande HelloAsso** et le **nom** ;
- une **case à cocher** quand la commande est retirée ;
- que ces commandes comptent dans la **recette** et le **stock**, mais **pas** dans l'argent attendu dans le tiroir.

## Récupérer les commandes HelloAsso

### Étape 1 (à faire en premier) : import du fichier d'export

Dans le back-office HelloAsso, on exporte les commandes du formulaire (boutique ou billetterie) en Excel ou CSV, puis on importe ce fichier dans l'appli.

Pourquoi commencer par là :

- aucun identifiant ni secret HelloAsso n'est stocké dans l'appli ;
- pas besoin d'internet le jour J : l'appli tourne sur un PC local, avec les téléphones sur le même Wi-Fi ;
- on peut réimporter sans risque : une commande déjà connue, reconnue à son numéro HelloAsso, n'est pas créée deux fois. On réimporte la veille ou le matin même pour avoir les dernières commandes.

**Avant de coder :** récupérer un vrai export de votre formulaire, même d'un événement passé, avec les noms et e-mails remplacés si besoin. Les colonnes changent selon le type de formulaire et les champs personnalisés, donc on code sur le vrai fichier plutôt qu'en devinant.

### Étape 2 (en option, plus tard) : API HelloAsso

HelloAsso a une API officielle (v5) :

- l'association crée un identifiant client (client ID) et un secret dans « Intégrations et API » du back-office ;
- l'appli obtient un jeton d'accès avec ces identifiants, puis lit les commandes du formulaire (`/v5/organizations/{association}/forms/{type}/{formulaire}/orders`) ;
- un bouton « Synchroniser » remplace alors l'import du fichier. La logique derrière reste la même : on reconnaît les commandes à leur numéro, on écarte celles qui ont été remboursées.

Limites :

- il faut internet au moment de synchroniser ;
- le secret doit être protégé : ne pas le mettre dans le dépôt git, le garder dans un fichier de configuration local ou dans les secrets utilisateur ;
- les notifications automatiques de HelloAsso (webhooks) **ne conviennent pas** : elles demandent que l'appli soit joignable depuis internet, ce qui n'est pas le cas du PC de la buvette.

## Modèle de données

### `PreCommande` (nouvelle entité, liée à un `Evenement`)

| Champ | Rôle |
|---|---|
| `NumeroHelloAsso` | numéro de commande HelloAsso, unique par événement : empêche les doublons à l'import |
| `Nom`, `Prenom` | pour la recherche au comptoir |
| `MontantPaye` | ce qui a été payé sur HelloAsso |
| `Statut` | à retirer / retirée / annulée (remboursée sur HelloAsso) |
| `RetireeLe` | date et heure du retrait, `null` tant que la commande n'est pas retirée |
| `CommandeId` | la `Commande` créée au retrait (voir plus bas) |
| `Lignes` | produit × quantité, avec le nom et le prix recopiés comme pour `LigneCommande` |

### Correspondance des articles HelloAsso avec les produits

Sur HelloAsso, les articles portent leurs propres noms (« Formule midi », « Crêpe sucre »…). On prévoit une table de correspondance par événement, à remplir une fois :

- article HelloAsso → un ou plusieurs produits × quantité (par exemple « Formule midi » = 1 sandwich + 1 boisson) ;
- à l'import, un article sans correspondance est **signalé**, jamais ignoré en silence.

### Mode de règlement d'une `Commande`

Remplacer le booléen `Offerte` par une énumération `ModeReglement` : `Caisse` / `Offerte` / `Prepayee`. Il faut une migration qui convertit l'existant (`Offerte = true` → `Offerte`, sinon `Caisse`).

## Le jour J : écran « Pré-commandes »

- Liste : n° HelloAsso, nom / prénom, contenu, case « Retirée ».
- Recherche par nom **ou** par numéro, parce qu'au comptoir les gens donnent l'un ou l'autre.
- Filtre « à retirer / retirées » et compteur (« 34 retirées sur 52 »).
- Cocher la case crée une `Commande` en mode `Prepayee` et enregistre l'heure. Décocher annule cette commande, pour corriger une erreur.
- Mise à jour en temps réel sur tous les téléphones grâce à `NotificationsBuvette` : une commande cochée sur un téléphone apparaît cochée sur les autres, ce qui évite de la remettre deux fois.

## Stock

Le stock restant affiché en caisse devient :

> stock prévu − vendu − pré-commandé pas encore retiré

Ainsi, les clients sans pré-commande ne peuvent pas prendre les articles déjà payés par d'autres. Le restant se déduit déjà des ventes : il suffit d'ajouter ce terme dans `BuvetteService.Restant`.

## Récapitulatif et comptage de caisse

- **Recette** = encaissé en caisse + prépayé HelloAsso, avec le détail affiché (« dont HelloAsso : 412 € »).
- **Attendu dans le tiroir** = fond de caisse + encaissé en caisse seulement. Sans cette séparation, le comptage afficherait un faux écart.
- **Pré-commandes non retirées** en fin de journée : les lister à part. L'argent est encaissé mais les produits sont restés ; l'association décide ensuite si elle rembourse. L'appli rend la situation visible sans trancher.
- Ajouter ces lignes à l'export CSV destiné au trésorier.

## Ordre de réalisation

1. Obtenir un export HelloAsso réel.
2. Modèle : `PreCommande`, correspondance des articles, `ModeReglement` (avec la migration).
3. Import du fichier, en évitant les doublons et en écartant les commandes remboursées.
4. Écran « Pré-commandes » avec la case « Retirée » et le temps réel.
5. Réservation du stock.
6. Récapitulatif, comptage et export CSV.
7. Plus tard, en option : synchronisation par l'API HelloAsso.
