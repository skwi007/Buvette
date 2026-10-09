#!/usr/bin/env bash
# Installation initiale de la buvette sur la VM Debian/Ubuntu de la Freebox. À lancer une seule fois :
#   sudo bash installer.sh buvette.mondomaine.fr
# Sans nom de domaine, la buvette est servie en HTTP sur le réseau local seulement (http://buvette.home) :
# relancer plus tard avec le domaine pour ouvrir l'accès depuis Internet en HTTPS.
# Le mot de passe d'accès est demandé au clavier (il n'apparaît ni à l'écran ni dans l'historique).
set -euo pipefail

DOMAINE="${1:-}"
ICI="$(cd "$(dirname "$0")" && pwd)"

read -rsp "Mot de passe d'accès à la buvette : " MOT_DE_PASSE; echo
[ -n "$MOT_DE_PASSE" ] || { echo "Mot de passe vide, abandon."; exit 1; }

echo "==> Paquets : Caddy (HTTPS), ICU (formats français), sqlite3 (sauvegardes)"
apt-get update -q
ICU=$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -1)
apt-get install -y -q caddy sqlite3 "$ICU"

echo "==> Compte système et dossiers"
id buvette >/dev/null 2>&1 || useradd --system --home-dir /opt/buvette --shell /usr/sbin/nologin buvette
install -d -o buvette -g buvette /opt/buvette/app /opt/buvette/sauvegardes

echo "==> Mot de passe"
# Seule la ligne du mot de passe est remplacée : la clé HelloAsso éventuelle est conservée.
umask 077
touch /etc/buvette.env
sed -i '/^Buvette__MotDePasse=/d' /etc/buvette.env
printf 'Buvette__MotDePasse=%s\n' "$MOT_DE_PASSE" >> /etc/buvette.env
umask 022

echo "==> Service"
install -m 644 "$ICI/buvette.service" /etc/systemd/system/buvette.service
systemctl daemon-reload
systemctl enable buvette

if [ -n "$DOMAINE" ]; then
    echo "==> HTTPS pour $DOMAINE"
    sed "s/DOMAINE/$DOMAINE/" "$ICI/Caddyfile" > /etc/caddy/Caddyfile
else
    echo "==> Pas de nom de domaine : HTTP sur le réseau local uniquement"
    sed "s/DOMAINE/:80/" "$ICI/Caddyfile" > /etc/caddy/Caddyfile
fi
systemctl reload caddy || systemctl restart caddy

echo "==> Sauvegarde quotidienne à 3 h 30, conservée 30 jours"
cat > /etc/cron.d/buvette <<'EOF'
30 3 * * * buvette sqlite3 /opt/buvette/app/buvette.db ".backup '/opt/buvette/sauvegardes/buvette-$(date +\%F).db'" && find /opt/buvette/sauvegardes -name 'buvette-*.db' -mtime +30 -delete
EOF

echo
echo "Installation terminée. Il reste à envoyer l'application avec deployer.ps1 (sans -Installer)."
echo "Pour changer le mot de passe plus tard : sudo nano /etc/buvette.env puis sudo systemctl restart buvette"
