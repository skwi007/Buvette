#!/usr/bin/env bash
# Remplace l'application par la version envoyée par deployer.ps1, sans toucher à la base ni aux clés.
#   sudo bash mettre-a-jour.sh /tmp/buvette-deploiement/buvette.tar.gz
set -euo pipefail

ARCHIVE="${1:?Usage : sudo bash mettre-a-jour.sh <archive.tar.gz>}"
APP=/opt/buvette/app

systemctl stop buvette || true

# Copie de sécurité avant toute mise à jour : une migration de schéma ne se défait pas.
if [ -f "$APP/buvette.db" ]; then
    sqlite3 "$APP/buvette.db" ".backup '/opt/buvette/sauvegardes/avant-maj-$(date +%F-%H%M%S).db'"
fi

# L'archive ne contient ni buvette.db ni cles/ : les ventes et les connexions sont conservées.
tar -xzf "$ARCHIVE" -C "$APP"
chown -R buvette:buvette "$APP"
chmod +x "$APP/Buvette"

systemctl start buvette
sleep 3
systemctl --no-pager --lines=5 status buvette
