# HelloAsso : configurer la clé API

La buvette lit les campagnes et les commandes de l'association directement sur HelloAsso,
grâce à l'API officielle (v5). Il faut pour cela une **clé API** de l'association, composée
d'un identifiant client et d'un secret, plus l'identifiant de l'association.

> **Le secret donne accès aux commandes, donc aux noms et e-mails des acheteurs.** Il ne va
> jamais dans le dépôt Git, ni dans un message, ni dans un fichier partagé. En cas de doute,
> le régénérer sur HelloAsso : l'ancien cesse aussitôt de fonctionner.

## 1. Créer la clé sur HelloAsso

1. Se connecter au back-office de l'association sur <https://admin.helloasso.com> avec un
   compte administrateur.
2. Menu **Mon compte › Intégrations et API** (le libellé exact peut varier légèrement).
3. Dans la partie **Ma clé API**, générer la clé, puis noter :
   - le **Client ID** (identifiant client) ;
   - le **Client Secret**, affiché parfois une seule fois : le copier tout de suite.

## 2. Trouver l'identifiant de l'association

C'est la partie de l'adresse de la page publique de l'association qui suit `/associations/` :

```text
https://www.helloasso.com/associations/les-p-tits-coeurs-de-rexa
                                       └──────── identifiant ───────┘
```

Il apparaît aussi dans l'adresse de chaque campagne.

## 3. Configurer le PC

Depuis un terminal PowerShell, à la racine du dépôt :

```powershell
dotnet user-secrets set "HelloAsso:ClientId"     "<Client ID>"     --project src/Buvette
dotnet user-secrets set "HelloAsso:ClientSecret" "<Client Secret>" --project src/Buvette
dotnet user-secrets set "HelloAsso:Organisation" "<identifiant>"   --project src/Buvette
```

Les valeurs sont rangées dans le profil Windows
(`%APPDATA%\Microsoft\UserSecrets\buvette-ptitscoeurs-de-rexa\secrets.json`), hors du dépôt.
PowerShell n'enregistre pas dans son historique les lignes contenant le mot « secret ».

Pour vérifier ce qui est enregistré, ou tout effacer :

```powershell
dotnet user-secrets list  --project src/Buvette
dotnet user-secrets clear --project src/Buvette
```

Redémarrer ensuite la buvette (`Lancer-buvette.cmd`).

## 4. Configurer la Freebox

Ajouter les trois lignes au fichier qui contient déjà le mot de passe :

```powershell
ssh -t freebox@192.168.1.6 "sudo nano /etc/buvette.env"
```

```ini
HelloAsso__ClientId=<Client ID>
HelloAsso__ClientSecret=<Client Secret>
HelloAsso__Organisation=<identifiant>
```

Enregistrer (**Ctrl+O**, **Entrée**), quitter (**Ctrl+X**), puis :

```powershell
ssh freebox@192.168.1.6 "sudo systemctl restart buvette"
```

Le fichier n'est lisible que par root, et `installer.sh` ne touche qu'à la ligne du mot de
passe : ces lignes survivent à une réinstallation.

## 5. Vérifier

Dans la buvette, bouton **HelloAsso** sur l'accueil, puis **Se connecter à HelloAsso** :

- **Connexion réussie** : la liste des campagnes de l'association s'affiche. **Voir les
  commandes** montre, pour une campagne, le nombre de commandes, le total, les articles
  vendus et la liste nominative. Les commandes annulées ou remboursées sont barrées et
  exclues des totaux.
- **« HelloAsso refuse l'identifiant ou le secret »** : erreur de copie (espace en trop,
  caractère manquant) ou clé régénérée depuis. Refaire l'étape 3 ou 4.
- **« HelloAsso ne trouve pas l'association »** : l'identifiant de l'étape 2 est inexact.
- **« Impossible de joindre HelloAsso »** : pas d'accès Internet sur le PC ou la VM.

Si HelloAsso n'est pas configuré, la page indique ce qui manque.

## Essayer sans toucher aux vraies données

HelloAsso propose un bac à sable (<https://www.helloasso-sandbox.com>) avec ses propres
comptes et clés. Pour l'utiliser, ajouter :

```powershell
dotnet user-secrets set "HelloAsso:UrlApi" "https://api.helloasso-sandbox.com" --project src/Buvette
```

La page HelloAsso affiche alors un badge « bac à sable ». Supprimer ce réglage
(`dotnet user-secrets remove "HelloAsso:UrlApi" --project src/Buvette`) pour revenir au réel.

## Une buvette alimentée par HelloAsso

### Créer ou lier la buvette

- **Nouvelle buvette** : page **HelloAsso**, **Se connecter**, puis **Créer la buvette** en
  face d'une billetterie ou d'une boutique. La buvette prend le nom et la date de la campagne.
- **Buvette existante** : page **Produits** de l'événement, carte « Préventes HelloAsso »,
  **Lier à une campagne HelloAsso**.

Dans les deux cas :

- chaque tarif de la campagne devient un produit de la buvette, au prix HelloAsso, y compris
  ceux que personne n'a encore achetés en ligne. Un produit qui porte déjà le même nom est
  relié au lieu d'être recréé ;
- chaque commande HelloAsso devient une **pré-commande**, avec le numéro HelloAsso, le nom
  de l'acheteur et ses articles.

Ensuite, ce sont des produits comme les autres : on peut changer leur prix au comptoir, les
retirer de la vente, et **ajouter des produits qui ne sont pas sur HelloAsso** (boissons…).

### Resynchroniser

Le bouton **Synchroniser** (page Produits) ou **Actualiser depuis HelloAsso** (page
Pré-commandes) relit la campagne. On peut le faire autant de fois que l'on veut :

- les nouvelles commandes s'ajoutent, sans doublon ;
- une commande remboursée entre-temps passe en « annulée » et libère son stock ;
- les prix, les stocks et les cases « retirée » saisis dans la buvette sont conservés.

Il faut Internet pour synchroniser, pas pour encaisser.

### Si les ventes en ligne sont encore ouvertes

On peut créer la buvette pendant que la campagne vend encore : les commandes passées après
la création arrivent à la synchronisation suivante. Rien n'est automatique : **entre deux
synchronisations, la buvette ne connaît pas les dernières ventes en ligne**, et la caisse
pourrait vendre au comptoir un article qui vient d'être payé sur HelloAsso.

Le plus simple est de **fermer les ventes en ligne la veille** et de synchroniser le matin.
Si elles restent ouvertes, cliquer sur **Actualiser** régulièrement, et en tout cas juste
avant l'ouverture.

### Le stock

Le **stock** saisi est la quantité totale prévue pour la journée, préventes comprises. Les
articles prépayés en sont déduits dès la synchronisation, qu'ils aient été retirés ou non :
la caisse ne peut pas vendre à un passant la tarte qu'un autre a déjà payée. La colonne
« Restant » de la page Produits indique combien sont prépayés.

### Le jour J : écran « Pré-commandes »

Accessible depuis l'accueil, la caisse et la page Produits :

- recherche par nom **ou** par numéro de commande, sans se soucier des accents ;
- une case à cocher par commande quand le client est servi ; elle apparaît cochée sur tous
  les téléphones à la fois, pour éviter de servir deux fois la même commande ;
- filtre « À retirer / Retirées / Toutes » et compteur « 12 retirées sur 52 ».

### Fin de journée

L'**Historique** sépare les deux sources d'argent :

- **Attendu en caisse** = fond de caisse + ventes au comptoir. Les préventes n'y sont pas :
  leur argent est sur le compte HelloAsso, et le comptage du tiroir ne montre donc pas de
  faux écart ;
- **Prépayé HelloAsso** et **Recette totale** (comptoir + HelloAsso) ;
- la liste des commandes **payées mais jamais retirées**, pour que l'association décide
  si elle rembourse.

L'export CSV reprend ces lignes.

### Délier

**Délier** (page Produits) détache la buvette de la campagne et oublie ses pré-commandes. Les
produits restent.

### Pas encore fait

Un article HelloAsso correspond à **un** produit. Une formule (« menu = tarte + boisson »)
n'est pas encore décomposée en plusieurs produits.
