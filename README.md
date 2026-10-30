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

## Démonstration en ligne sur la Freebox Delta

Pour montrer la buvette aux membres de l'association avant l'événement, elle peut tourner
en permanence dans une machine virtuelle de la Freebox Delta, accessible depuis Internet
en HTTPS et protégée par un mot de passe.

**Le guide complet, écran par écran, avec la maintenance et le dépannage, est dans
[docs/deploiement-freebox.md](docs/deploiement-freebox.md).** Ci-dessous, l'essentiel.

Le jour J, on reste de préférence sur le PC portable : Blazor garde une connexion
permanente avec le serveur, et une 4G faible sur le lieu de l'événement afficherait
« reconnexion… » en plein encaissement.

### Le mot de passe

Il n'existe que si la variable d'environnement `Buvette__MotDePasse` est définie. Sans
elle, rien ne change : le PC du jour J reste ouvert au réseau local comme avant.

Une fois défini, chaque appareil le saisit une fois et reste connecté un mois. La
majuscule mise d'office par le clavier des tablettes est ignorée, de même que les espaces
autour. Chaque erreur coûte une seconde d'attente, ce qui rend inutile l'essai en masse.
Le lien *Se déconnecter* apparaît alors en haut à droite.

### Préparer la Freebox (une seule fois)

1. **Créer la VM** dans Freebox OS, menu *VMs* : image Debian ou Ubuntu (la Freebox Delta
   a un processeur ARM, l'image proposée l'est aussi), 1 Go de mémoire suffit. Y coller sa
   clé SSH publique (`ssh-keygen` sous Windows pour en créer une, puis le contenu de
   `%USERPROFILE%\.ssh\id_ed25519.pub`).
2. **Fixer l'adresse de la VM** : *Paramètres de la Freebox › DHCP › Baux statiques*.
3. **Avoir une IPv4 publique entière** : si l'abonnement est en IP partagée, demander l'IP
   *full-stack* dans l'espace abonné Free. Sans elle, les ports 80 et 443 ne sont pas
   disponibles.
4. **Rediriger les ports** TCP 80 et 443 vers l'adresse de la VM : *Paramètres de la
   Freebox › Gestion des ports*. L'accès à distance de Freebox OS doit utiliser un autre port.
5. **Un nom de domaine** pointant vers l'IP publique de la Freebox : sous-domaine d'un domaine
   de l'association, ou nom gratuit (DuckDNS, par exemple). Le certificat HTTPS est ensuite
   obtenu et renouvelé automatiquement par Caddy.

### Installer et mettre à jour

Depuis le PC de développement, à la racine du dépôt :

```powershell
# Première fois : installe Caddy, le service et le mot de passe (demandé au clavier).
.\deploy\freebox\deployer.ps1 -Vm utilisateur@192.168.1.60 -Installer -Domaine buvette.mondomaine.fr

# Ensuite, après chaque évolution du code :
.\deploy\freebox\deployer.ps1 -Vm utilisateur@192.168.1.60
```

Le script publie l'application pour Linux ARM64 (autonome, sans .NET à installer sur la VM),
l'envoie et redémarre le service. La base de la VM n'est jamais écrasée, et elle est copiée
dans `/opt/buvette/sauvegardes/` avant chaque mise à jour et chaque nuit (30 jours conservés).

| Sur la VM | Rôle |
| --- | --- |
| `/opt/buvette/app/` | Application, base `buvette.db` et clés de connexion `cles/` |
| `/opt/buvette/sauvegardes/` | Copies de la base |
| `/etc/buvette.env` | Mot de passe (changer puis `sudo systemctl restart buvette`) |
| `/etc/caddy/Caddyfile` | Nom de domaine et HTTPS |
| `journalctl -u buvette -f` | Journal de l'application en direct |

## Préventes HelloAsso

La page **HelloAsso** (bouton sur l'accueil) se connecte à l'API HelloAsso de l'association
et affiche ses campagnes et leurs commandes. **Créer la buvette** transforme une billetterie
ou une boutique en buvette : ses articles deviennent des produits, ses commandes des
pré-commandes à cocher au comptoir, déduites du stock et comptées à part du tiroir.

La configuration de la clé API, sur le PC comme sur la Freebox, et le mode d'emploi sont
décrits dans [docs/helloasso.md](docs/helloasso.md).

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

**Consommations offertes.** L'interrupteur *Offert* sert aux bénévoles et aux organisateurs :
la commande sort du stock mais n'entre pas dans la recette attendue en caisse. Le champ de
rendu de monnaie disparaît, et l'interrupteur se remet seul à zéro après validation — un
oubli coûterait une recette sans que personne ne s'en aperçoive.

### 3. Récapituler

La page **Historique** donne, pour chaque produit, la quantité vendue et le montant, puis :

- le **total des ventes**,
- le **montant attendu dans la caisse** (fond de caisse + ventes), à comparer au comptage réel.

Quand des consommations ont été offertes, le tableau distingue trois colonnes : ce qui a
été **vendu**, ce qui a été **offert**, et le total **sorti du stock**. Seul le vendu entre
dans le montant attendu en caisse.

Le détail commande par commande permet d'annuler une saisie erronée. Le récapitulatif
s'imprime, ou s'exporte en CSV pour Excel (séparateur `;`, décimales à la virgule).

### 4. Compter la caisse

En fin de journée, saisir dans **Comptage de la caisse** ce que contient réellement le
tiroir, fond de caisse compris. L'application affiche l'écart :

- **La caisse tombe juste** — rien à signaler ;
- **Il manque X €** — rendu de monnaie erroné, commande oubliée, consommation offerte
  non saisie ;
- **Excédent de X €** — commande encaissée deux fois, ou monnaie mal rendue en votre faveur.

Le montant compté et l'écart sont conservés, et figurent dans l'export CSV pour le trésorier.

## Points de fonctionnement à connaître

- **Les prix sont figés à la vente.** Chaque ligne de commande recopie le nom et le prix
  du produit. Corriger un prix ou supprimer un produit en cours de journée ne modifie
  jamais les ventes déjà encaissées ; un produit vendu à deux prix apparaît sur deux
  lignes du récapitulatif.
- **Clôturer** une buvette bloque toute nouvelle vente sans rien effacer. L'historique
  reste consultable et la buvette peut être rouverte.
- **Annuler une commande la supprime définitivement** des totaux : c'est le seul moyen
  de corriger une erreur de caisse. Si le comptage était déjà saisi, un écart apparaît
  aussitôt — c'est voulu, il signale que les deux ne concordent plus.
- **Une commande offerte garde la trace de sa valeur**, pour savoir en fin de journée ce
  que les gratuités ont représenté.

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

204 tests xUnit couvrent les règles métier, dans `tests/Buvette.Tests/`.
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
- l'impossibilité de vendre plus que le stock, même depuis plusieurs caisses à la fois ;
- les gratuités hors recette mais bien décomptées du stock ;
- le calcul de l'écart de caisse et son signe.

### Tests dans le navigateur

`tests/Buvette.E2E/` pilote l'application dans un vrai Chromium avec Playwright : création
d'un événement et de sa carte, encaissement et monnaie à rendre, consommations offertes,
stock épuisé, mise à jour en direct entre deux caisses, comptage du tiroir, export CSV,
connexion par mot de passe et icône de l'onglet. L'application démarre sur un port libre
avec une base temporaire : la base de l'association n'est jamais touchée, et HelloAsso
n'est jamais appelé.

Une seule fois par poste, après la première compilation, installer le navigateur :

```powershell
pwsh tests/Buvette.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
```

## Structure

| Chemin | Rôle |
| --- | --- |
| `Buvette.slnx` | Solution regroupant l'application et ses tests |
| `Lancer-buvette.cmd` | Démarrage en un double-clic le jour de l'événement |
| `deploy/freebox/` | Installation et mise à jour de la démonstration sur la Freebox |
| `src/Buvette/Program.cs` | Démarrage, base de données, export CSV |
| `src/Buvette/Data/Modeles.cs` | Événement, produit, commande, ligne de commande |
| `src/Buvette/Data/BuvetteContext.cs` | Configuration EF Core et relations |
| `src/Buvette/Data/BuvetteService.cs` | Règles métier : encaissement, récapitulatif |
| `src/Buvette/Data/Prix.cs` | Lecture des montants saisis (virgule ou point) |
| `src/Buvette/Data/AdressesReseau.cs` | Tri des adresses réseau proposées aux tablettes |
| `src/Buvette/Data/AccesProtege.cs` | Vérification du mot de passe facultatif |
| `src/Buvette/HelloAsso/` | Client de l'API HelloAsso (jeton, campagnes, commandes) |
| `src/Buvette/Data/Migrations/` | Migrations EF Core du schéma |
| `src/Buvette/Components/Pages/Home.razor` | Liste et création des événements |
| `src/Buvette/Components/Pages/EvenementPage.razor` | Réglages et carte des produits |
| `src/Buvette/Components/Pages/Caisse.razor` | Écran de vente |
| `src/Buvette/Components/Pages/Historique.razor` | Récapitulatif et détail des commandes |
| `tests/Buvette.Tests/` | Tests xUnit des règles métier |
| `tests/Buvette.E2E/` | Tests Playwright des écrans, dans un vrai navigateur |

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
