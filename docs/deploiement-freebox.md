# Démonstration en ligne sur la Freebox Delta

Ce guide décrit, étape par étape, comment la buvette a été installée dans une machine
virtuelle de la Freebox Delta pour être accessible depuis Internet, en HTTPS et protégée
par un mot de passe. Il sert à refaire l'installation, à la maintenir et à la dépanner.

> **À quoi sert cette installation ?** À montrer l'application aux membres de l'association
> avant l'événement. **Le jour J, on encaisse plutôt sur le PC portable**, sur place : Blazor
> garde une connexion permanente avec le serveur, et une 4G faible sur le lieu de
> l'événement afficherait « reconnexion… » en plein encaissement. La base de la Freebox et
> celle du PC sont indépendantes : ce que les membres testent en ligne n'arrive jamais dans
> la caisse du jour J.

## Notre installation

| Élément | Valeur |
| --- | --- |
| Adresse publique | <https://buvette-ptitscoeurs.duckdns.org> |
| Adresse sur le réseau de la maison | <http://buvette.home> ou <http://192.168.1.6> |
| VM | `buvette` — Debian 12 (Bookworm), 2 CPU, 957 Mo, disque 10 Go |
| Fichier disque de la VM | `Freebox/VMs/buvette.qcow2` |
| Adresse MAC de la VM | `0e:b5:9a:87:66:70` |
| IP locale fixe | `192.168.1.6` (bail DHCP statique) |
| IP publique | `82.64.61.38` (IPv4 full-stack Free, fixe) |
| Connexion SSH | `ssh freebox@192.168.1.6` (clé `%USERPROFILE%\.ssh\id_ed25519`) |
| Ports ouverts | TCP 80 et 443 → 192.168.1.6 |
| Nom de domaine | DuckDNS, compte de Laurent |
| Certificat HTTPS | Let's Encrypt, obtenu et renouvelé automatiquement par Caddy |

Le mot de passe de la buvette n'est volontairement écrit nulle part dans le dépôt : il vit
uniquement dans `/etc/buvette.env` sur la VM.

## Comment ça marche

```text
Téléphone / tablette ──HTTPS──▶ Freebox (ports 80, 443) ──▶ VM « buvette » 192.168.1.6
                                                              │
                                                              ├─ Caddy  : HTTPS, certificat
                                                              │     │ http://127.0.0.1:5000
                                                              │     ▼
                                                              └─ Buvette (service systemd)
                                                                   base buvette.db
```

- **Caddy** reçoit le trafic Internet, gère le certificat HTTPS et relaie vers l'application.
  L'application elle-même n'écoute que sur la VM (`127.0.0.1:5000`) : elle n'est jamais
  joignable directement depuis Internet.
- **Le mot de passe** n'est activé que si la variable `Buvette__MotDePasse` est définie
  (voir [Le mot de passe](#le-mot-de-passe)). Sur le PC du jour J, elle ne l'est pas, et rien
  ne change.
- L'application est publiée **autonome pour Linux ARM64** (processeur de la Freebox Delta) :
  rien à installer côté .NET sur la VM.

### Fichiers du dépôt

| Fichier | Rôle |
| --- | --- |
| `deploy/freebox/deployer.ps1` | Lancé depuis le PC : publie, envoie et (ré)installe |
| `deploy/freebox/installer.sh` | Installation initiale sur la VM (Caddy, service, mot de passe, sauvegardes) |
| `deploy/freebox/mettre-a-jour.sh` | Remplace l'application sans toucher à la base |
| `deploy/freebox/buvette.service` | Service systemd : démarrage automatique de l'application |
| `deploy/freebox/Caddyfile` | Modèle de configuration HTTPS |
| `src/Buvette/Data/AccesProtege.cs` | Vérification du mot de passe |

### Sur la VM

| Chemin | Rôle |
| --- | --- |
| `/opt/buvette/app/` | Application, base `buvette.db`, clés de connexion `cles/` |
| `/opt/buvette/sauvegardes/` | Sauvegardes quotidiennes et avant chaque mise à jour |
| `/etc/buvette.env` | Mot de passe (lisible par root seulement) |
| `/etc/caddy/Caddyfile` | Nom de domaine et HTTPS |
| `/etc/systemd/system/buvette.service` | Service de l'application |
| `/etc/cron.d/buvette` | Sauvegarde automatique à 3 h 30 |

---

## Installation pas à pas

Prérequis côté PC : le SDK .NET 10, OpenSSH (inclus dans Windows 10/11) et une clé SSH.
Pour vérifier la clé, ou en créer une si besoin :

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub     # affiche la clé publique
ssh-keygen -t ed25519                                # seulement si la commande ci-dessus échoue
```

### Étape 1 — Créer la VM

Freebox OS (version 4.12 au moment de l'installation) › **VMs** › **Ajouter une VM**.

**Écran 1**

| Champ | Valeur |
| --- | --- |
| Nom | `buvette` |
| CPUs | 2 |
| RAM (Mo) | 957 (le maximum proposé) |
| Installation | *Choisir un système d'exploitation pré-installé parmi une liste* |

**Écran 2**

| Champ | Valeur |
| --- | --- |
| Système pré-installé | Debian 12 (Bookworm) |
| Utilisateur par défaut | `freebox` |
| Clé SSH | Coller le contenu **complet** de `id_ed25519.pub`, sur une ligne |
| Mot de passe | **Laisser vide** — seule la clé permet d'entrer |
| Accès aux disques Freebox | Coché |

> Ne jamais coller le fichier `id_ed25519` (sans `.pub`) : c'est la clé privée.

**Écran 3** — après le téléchargement de l'image (100 %) : cocher **Redimensionner le
disque**, taille **10 Go**, puis **Terminer**.

La VM apparaît dans la liste avec *Cloud-init* coché : c'est lui qui crée l'utilisateur
`freebox` et installe la clé au premier démarrage. Cliquer sur **OK**, puis **Allumer**.
Le premier démarrage prend une à deux minutes.

### Étape 2 — Fixer l'adresse IP de la VM

1. L'IP de la VM s'affiche sous son nom dans la fenêtre **VMs** (ici `192.168.1.6`), ou
   dans **Périphériques réseau**, où elle apparaît sous le nom `buvette`.
2. **Paramètres de la Freebox › Réseau local / DHCP › Baux statiques › Ajouter un bail
   DHCP statique** :
   - Adresse MAC : celle de la VM (`0e:b5:9a:87:66:70`)
   - Adresse IP : `192.168.1.6`
   - Commentaire : `VM buvette`
   - **Sauvegarder**, sans ajouter d'option DHCP.

Sans ce bail, l'IP pourrait changer après un redémarrage et la redirection de ports
pointerait dans le vide.

### Étape 3 — Vérifier l'accès SSH

Depuis un terminal PowerShell du PC :

```powershell
ssh freebox@192.168.1.6
```

À la première connexion, répondre `yes` à la question sur l'empreinte. Une fois connecté :

```bash
sudo whoami      # doit répondre « root » sans demander de mot de passe
exit
```

Le nom `buvette.home` fonctionne aussi à la place de l'IP.

### Étape 4 — Première installation, sur le réseau local

Dans VS Code : **Terminal › Nouveau terminal**, choisir **PowerShell** (l'invite doit
ressembler à `PS C:\Dev\Buvette>`), puis à la racine du dépôt :

```powershell
.\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.6 -Installer
```

Si Windows refuse (« l'exécution de scripts est désactivée sur ce système ») :

```powershell
pwsh -ExecutionPolicy Bypass -File .\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.6 -Installer
```

Le script :

1. publie l'application pour Linux ARM64 dans `publie-freebox/` (ignoré par Git) ;
2. retire toute base de données et les réglages de développement de la publication ;
3. envoie l'archive et les scripts sur la VM ;
4. installe Caddy, la bibliothèque ICU (formats français) et sqlite3 ;
5. crée le compte système `buvette` et le dossier `/opt/buvette/` ;
6. **demande le mot de passe de la buvette** — la saisie est masquée, rien ne s'affiche ;
7. installe et active le service, configure Caddy et la sauvegarde quotidienne ;
8. démarre l'application.

Compter 3 à 5 minutes. Sans `-Domaine`, Caddy sert l'application en **HTTP sur le réseau
local uniquement** : ouvrir <http://buvette.home> doit afficher la page de connexion.

### Étape 5 — Vérifier que l'IP publique n'est pas partagée

Free attribue parfois une IPv4 **partagée** entre plusieurs abonnés, avec seulement une
plage de ports utilisable. Les ports 80 et 443 sont alors indisponibles.

Le plus simple est de tenter l'étape suivante : si la Freebox refuse les ports 80 et 443 en
indiquant une plage autorisée, demander l'**IP full-stack** dans l'espace abonné Free
(rubrique *Ma Freebox*). L'activation peut prendre jusqu'à 30 minutes et un redémarrage
de la box.

Dans notre cas, les ports ont été acceptés : l'IP est full-stack, et donc fixe.

### Étape 6 — Rediriger les ports 80 et 443

**Paramètres de la Freebox › Gestion des ports › Ajouter une redirection**, deux fois :

| Champ | Règle 1 | Règle 2 |
| --- | --- | --- |
| IP de destination | 192.168.1.6 (`buvette`) | 192.168.1.6 (`buvette`) |
| Redirection active | ✓ | ✓ |
| IP source | Toutes | Toutes |
| Protocole | TCP | TCP |
| Port de début / de fin | 80 / 80 | 443 / 443 |
| Port de destination | 80 | 443 |
| Commentaire | `buvette http` | `buvette https` |

Puis **OK**. Ne pas activer la DMZ.

Si la Freebox signale qu'un port est déjà utilisé, c'est l'accès à distance de Freebox OS :
lui attribuer un autre port dans **Paramètres › Accès à distance**, puis recommencer.

Le port 80 reste nécessaire même si tout passe en HTTPS : Let's Encrypt l'utilise pour
vérifier le domaine, et Caddy y redirige les visiteurs vers `https://`.

### Étape 7 — Le nom de domaine (DuckDNS)

1. Se connecter sur <https://www.duckdns.org> (compte Google, GitHub…).
2. Dans **sub domain**, saisir `buvette-ptitscoeurs`, puis **add domain**.
3. Vérifier que **current ip** affiche l'IP publique de la Freebox (`82.64.61.38`) ; sinon
   la corriger et cliquer sur **update ip**.

L'IP full-stack de Free étant fixe, aucun programme de mise à jour DuckDNS n'est nécessaire.
Pour connaître l'IP publique depuis la VM : `curl -4 https://api.ipify.org`.

### Étape 8 — Passer en HTTPS

```powershell
.\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.6 -Installer -Domaine buvette-ptitscoeurs.duckdns.org
```

Le script redemande le mot de passe (on peut remettre le même). Caddy obtient le
certificat Let's Encrypt en quelques secondes.

### Étape 9 — Vérifier

- Depuis un téléphone **en 4G, Wi-Fi coupé** : <https://buvette-ptitscoeurs.duckdns.org>
  affiche la page de connexion avec le cadenas ; après le mot de passe, la buvette.
- `http://buvette-ptitscoeurs.duckdns.org` redirige vers `https://`.
- Ouvrir la caisse sur deux appareils : une vente sur l'un apparaît sur l'autre.

Pour vérifier le certificat depuis la VM :

```powershell
ssh freebox@192.168.1.6 "echo | openssl s_client -connect 127.0.0.1:443 -servername buvette-ptitscoeurs.duckdns.org 2>/dev/null | openssl x509 -noout -issuer -enddate"
```

Résultat attendu : `O = Let's Encrypt`, avec une date d'expiration à environ trois mois.

> Sur le PC de Laurent, le navigateur affiche un certificat « Avast Web/Mail Shield » :
> c'est l'antivirus qui inspecte le trafic HTTPS. Les autres appareils voient bien le
> certificat Let's Encrypt.

---

## Le mot de passe

- **Activation** : uniquement si la variable d'environnement `Buvette__MotDePasse` est
  définie. Sur la VM, elle est lue depuis `/etc/buvette.env` par le service.
- **Toute l'application est protégée** : pages, connexion temps réel de Blazor, export CSV.
  Seuls les styles, le logo et les scripts restent publics, pour afficher la page de connexion.
- **Saisie tolérante** : la casse et les espaces autour sont ignorés (les tablettes mettent
  d'office une majuscule au premier mot).
- **Connexion mémorisée un mois** sur chaque appareil, grâce à un cookie. Les clés qui le
  chiffrent sont conservées dans `/opt/buvette/app/cles/` : un redémarrage ne déconnecte
  personne.
- **Une seconde de pénalité** à chaque erreur, pour décourager les essais en masse. Des
  robots visitent tout site public dans les secondes qui suivent sa mise en ligne : éviter
  un mot de passe trop évident comme `buvette`.
- Le lien **Se déconnecter** apparaît en haut à droite quand le mot de passe est actif.

### Changer le mot de passe

```powershell
ssh -t freebox@192.168.1.6 "sudo nano /etc/buvette.env"
```

Modifier la ligne `Buvette__MotDePasse=…`, enregistrer (**Ctrl+O**, **Entrée**), quitter
(**Ctrl+X**), puis :

```powershell
ssh freebox@192.168.1.6 "sudo systemctl restart buvette"
```

Les appareils déjà connectés le restent ; seules les nouvelles connexions demandent le
nouveau mot de passe. Pour déconnecter tout le monde, supprimer aussi les clés avant le
redémarrage : `sudo rm -rf /opt/buvette/app/cles`.

### Tester le mot de passe sur le PC

```powershell
$env:Buvette__MotDePasse = 'essai'; dotnet run --project src/Buvette
```

Fermer le terminal (ou `Remove-Item Env:Buvette__MotDePasse`) pour revenir au fonctionnement
sans mot de passe.

---

## Maintenance

### Mettre à jour l'application

Après une modification du code, depuis la racine du dépôt :

```powershell
.\deploy\freebox\deployer.ps1 -Vm freebox@192.168.1.6
```

Sans `-Installer`, le script publie, envoie et redémarre seulement l'application. Avant
de la remplacer, il copie la base dans `/opt/buvette/sauvegardes/avant-maj-<date>.db` :
une migration de schéma ne se défait pas. La base et les clés de connexion ne sont jamais
écrasées.

### Commandes utiles

| Besoin | Commande (depuis le PC) |
| --- | --- |
| État de l'application | `ssh freebox@192.168.1.6 "systemctl status buvette"` |
| Journal en direct | `ssh -t freebox@192.168.1.6 "journalctl -u buvette -f"` |
| Journal de Caddy (HTTPS) | `ssh -t freebox@192.168.1.6 "journalctl -u caddy -n 50"` |
| Redémarrer l'application | `ssh freebox@192.168.1.6 "sudo systemctl restart buvette"` |
| Arrêter l'application | `ssh freebox@192.168.1.6 "sudo systemctl stop buvette"` |
| Mises à jour de Debian | `ssh -t freebox@192.168.1.6 "sudo apt update && sudo apt upgrade"` |

Les mises à jour de sécurité de Debian sont à passer de temps en temps, la VM étant
exposée sur Internet.

### Sauvegardes

- **Chaque nuit à 3 h 30** : `/opt/buvette/sauvegardes/buvette-AAAA-MM-JJ.db`, conservées
  30 jours.
- **Avant chaque mise à jour** : `/opt/buvette/sauvegardes/avant-maj-<date-heure>.db`
  (non supprimées automatiquement).

Les copies sont faites avec `sqlite3 .backup`, sûr même pendant que l'application tourne.

**Récupérer une sauvegarde sur le PC** :

```powershell
ssh freebox@192.168.1.6 "ls -l /opt/buvette/sauvegardes"
scp freebox@192.168.1.6:/opt/buvette/sauvegardes/buvette-2026-10-03.db .
```

**Restaurer une sauvegarde sur la VM** :

```bash
ssh -t freebox@192.168.1.6
sudo systemctl stop buvette
sudo cp /opt/buvette/sauvegardes/buvette-2026-10-03.db /opt/buvette/app/buvette.db
sudo rm -f /opt/buvette/app/buvette.db-wal /opt/buvette/app/buvette.db-shm
sudo chown buvette:buvette /opt/buvette/app/buvette.db
sudo systemctl start buvette
```

**Repartir d'une base vide** (après la démonstration, par exemple) : même procédure, mais
avec `sudo rm -f /opt/buvette/app/buvette.db*` à la place de la copie. L'application recrée
une base vide au démarrage.

### Couper l'accès depuis Internet

Pour mettre la démonstration hors ligne sans rien désinstaller : désactiver les deux
redirections dans **Gestion des ports** (ou arrêter la VM dans **VMs**). Les réactiver
suffit à la remettre en ligne.

---

## Dépannage

| Symptôme | Cause probable et solution |
| --- | --- |
| `ssh` : *Permission denied (publickey)* | La clé collée à la création de la VM n'est pas celle du PC. Recréer la VM, ou ajouter la clé via la console Freebox OS. |
| `ssh` : *Connection timed out* | VM éteinte, ou IP changée : vérifier dans **VMs** et le bail statique. |
| Script : *exécution de scripts désactivée* | Lancer avec `pwsh -ExecutionPolicy Bypass -File …`. |
| Script : `sudo` demande un mot de passe | L'utilisateur n'a pas les droits sans mot de passe : se connecter et lancer `sudo bash /tmp/buvette-deploiement/installer.sh <domaine>` à la main. |
| Page **502 Bad Gateway** | L'application est arrêtée ou en train de redémarrer. Attendre 10 secondes, sinon lire `journalctl -u buvette -n 50`. |
| Erreur de certificat ou site injoignable depuis la 4G | Vérifier que DuckDNS pointe vers la bonne IP (`nslookup buvette-ptitscoeurs.duckdns.org`), que les redirections 80 et 443 sont actives, puis lire `journalctl -u caddy -n 50`. |
| La page s'affiche mais les boutons ne répondent pas | La connexion temps réel (WebSocket) ne passe pas : souvent un réseau d'entreprise ou un Wi-Fi public filtrant. Essayer en 4G. |
| « Reconnexion… » fréquent | Réseau mobile instable : c'est la limite de Blazor Server, et la raison de garder le PC sur place le jour J. |
| Mot de passe refusé alors qu'il est correct | Vérifier `/etc/buvette.env` (pas de guillemets ni d'espace parasite) et redémarrer le service. |
| Tout le monde est déconnecté après une mise à jour | Le dossier `/opt/buvette/app/cles/` a été supprimé : se reconnecter suffit. |
