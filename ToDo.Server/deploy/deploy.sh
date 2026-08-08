#!/usr/bin/env bash
# Deploys the sync server to a Linux VPS (Ubuntu/Debian) behind Caddy.
#
# Fill in HOST and DOMAIN below, then run from the repo root:
#   ./ToDo.Server/deploy/deploy.sh
#
# Prereqs on the VPS: dotnet NOT needed (self-contained publish); rsync, openssl,
# and a running Caddy with the caddy binary available to reload.
set -euo pipefail

HOST="user@your-vps"                       # SSH target
DOMAIN="sync.example.com"                  # DNS already pointing at the VPS
REMOTE_APP_DIR="/opt/todo-sync"
REMOTE_DATA_DIR="/var/lib/todo-sync"

cd "$(dirname "$0")/../.."                 # repo root

echo "==> 1/5 publish (linux-x64, self-contained)"
dotnet publish ToDo.Server/ToDo.Server.csproj -c Release -r linux-x64 \
  --self-contained true -o /tmp/todo-sync-publish

echo "==> 2/5 copy binaries"
rsync -avz --delete /tmp/todo-sync-publish/ "$HOST:$REMOTE_APP_DIR/"
rsync -avz ToDo.Server/deploy/todo-sync.service "$HOST:/tmp/todo-sync.service"
rsync -avz ToDo.Server/deploy/Caddyfile "$HOST:/tmp/todo-sync-caddy"

echo "==> 3/5 generate shared sync key (kept if it already exists)"
ssh "$HOST" "if [ ! -f /etc/todo-sync.env ]; then
  umask 077
  printf 'SYNC_KEY=%s\n' \"\$(openssl rand -hex 24)\" > /etc/todo-sync.env
fi"

echo "==> 4/5 install systemd unit + data dir"
ssh "$HOST" "mkdir -p $REMOTE_APP_DIR $REMOTE_DATA_DIR &&
  install -m 644 /tmp/todo-sync.service /etc/systemd/system/todo-sync.service &&
  systemctl daemon-reload &&
  systemctl enable --now todo-sync"

echo "==> 5/5 wire up Caddy"
ssh "$HOST" "sed 's|YOUR_DOMAIN|$DOMAIN|' /tmp/todo-sync-caddy |
  (grep -q '$DOMAIN' /etc/caddy/Caddyfile || tee -a /etc/caddy/Caddyfile) &&
  systemctl reload caddy"

echo "==> done. Health check:"
curl -fsS "https://$DOMAIN/healthz"
echo
echo "The client sync key lives in $HOST:/etc/todo-sync.env (SYNC_KEY=...)."
