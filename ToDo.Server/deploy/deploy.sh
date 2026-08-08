#!/usr/bin/env bash
# Deploys the sync server to a Linux VPS (Ubuntu/Debian) behind Caddy.
#
#   ./ToDo.Server/deploy/deploy.sh
#
# Deploy target (SSH host + domain) is read from a LOCAL, gitignored file
# ToDo.Server/deploy/deploy.local — create it with:
#   HOST="root@1.2.3.4"
#   DOMAIN="sync.example.com"
# (If absent, falls back to the placeholders below — never put real values in
# the committed script.)
#
# Prereqs on the VPS: openssl, a running Caddy (systemd). dotnet NOT needed on the
# VPS (self-contained publish). Binaries are transferred as one tar.gz via scp,
# incrementally (only files whose md5 changed) on re-deploys — no rsync needed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
[ -f "$SCRIPT_DIR/deploy.local" ] && . "$SCRIPT_DIR/deploy.local"

HOST="${HOST:-user@your-vps}"              # SSH target (overridden by deploy.local)
DOMAIN="${DOMAIN:-sync.example.com}"       # A record already points it at the VPS
REMOTE_APP_DIR="/opt/todo-sync"
REMOTE_DATA_DIR="/var/lib/todo-sync"

cd "$SCRIPT_DIR/../.."                     # repo root

echo "==> 1/5 publish (linux-x64, self-contained)"
rm -rf /tmp/todo-sync-publish
dotnet publish ToDo.Server/ToDo.Server.csproj -c Release -r linux-x64 \
  --self-contained true -o /tmp/todo-sync-publish

# ─── 2/5 copy binaries: incremental. The publish output is ~109MB but 99% of it
# is the immutable .NET runtime + framework; only the few app DLLs actually change
# between releases. We keep an md5 manifest (~/.todo-sync-publish-manifest.md5) of
# the last upload and send only changed/new files (deleted ones are removed).
echo "==> 2/5 copy binaries (incremental)"
PUBDIR=/tmp/todo-sync-publish
MANIFEST="$HOME/.todo-sync-publish-manifest.md5"

# Normalize "hash *path" → "hash  path" (some md5sum builds use binary-mode stars).
( cd "$PUBDIR" && find . -type f -exec md5sum {} \; ) | sed 's/ \*/  /' > /tmp/current-manifest.md5

if [ ! -f "$MANIFEST" ]; then
    echo "    (first deploy — full upload, $(du -sh "$PUBDIR" | cut -f1))"
    tar -czf /tmp/todo-sync-publish.tar.gz -C "$PUBDIR" .
    scp /tmp/todo-sync-publish.tar.gz "$HOST:/tmp/todo-sync-publish.tar.gz"
    ssh "$HOST" "mkdir -p $REMOTE_APP_DIR && rm -rf $REMOTE_APP_DIR/* &&
      tar -xzf /tmp/todo-sync-publish.tar.gz -C $REMOTE_APP_DIR && rm -f /tmp/todo-sync-publish.tar.gz"
else
    awk 'NR==FNR{old[$2]=$1; next} {if (old[$2] != $1) print $2}' "$MANIFEST" /tmp/current-manifest.md5 > /tmp/changed-files.txt
    awk '{print $2}' "$MANIFEST"    | sort > /tmp/old-paths.txt
    awk '{print $2}' /tmp/current-manifest.md5 | sort > /tmp/current-paths.txt
    comm -23 /tmp/old-paths.txt /tmp/current-paths.txt > /tmp/deleted-files.txt

    if [ -s /tmp/changed-files.txt ]; then
        echo "    uploading $(wc -l < /tmp/changed-files.txt) changed file(s): $(tr '\n' ' ' < /tmp/changed-files.txt)"
        tar -czf /tmp/todo-sync-publish.tar.gz -C "$PUBDIR" -T /tmp/changed-files.txt
        scp /tmp/todo-sync-publish.tar.gz "$HOST:/tmp/todo-sync-publish.tar.gz"
        ssh "$HOST" "mkdir -p $REMOTE_APP_DIR &&
          tar -xzf /tmp/todo-sync-publish.tar.gz -C $REMOTE_APP_DIR && rm -f /tmp/todo-sync-publish.tar.gz"
    else
        echo "    (no changed files)"
    fi

    if [ -s /tmp/deleted-files.txt ]; then
        echo "    removing $(wc -l < /tmp/deleted-files.txt) deleted file(s)"
        scp /tmp/deleted-files.txt "$HOST:/tmp/deleted-files.txt"
        ssh "$HOST" "cd $REMOTE_APP_DIR && xargs -r rm -f < /tmp/deleted-files.txt && rm -f /tmp/deleted-files.txt"
    fi
fi
cp /tmp/current-manifest.md5 "$MANIFEST"

scp ToDo.Server/deploy/todo-sync.service "$HOST:/tmp/todo-sync.service"
scp ToDo.Server/deploy/Caddyfile "$HOST:/tmp/todo-sync-caddy"

echo "==> 3/5 generate shared sync key (kept if it already exists)"
ssh "$HOST" "if [ ! -f /etc/todo-sync.env ]; then
  umask 077
  printf 'SYNC_KEY=%s\n' \"\$(openssl rand -hex 24)\" > /etc/todo-sync.env
fi"

echo "==> 4/5 install systemd unit + data dir"
ssh "$HOST" "mkdir -p $REMOTE_APP_DIR $REMOTE_DATA_DIR &&
  install -m 644 /tmp/todo-sync.service /etc/systemd/system/todo-sync.service &&
  systemctl daemon-reload &&
  systemctl enable todo-sync &&
  systemctl restart todo-sync"

echo "==> 5/5 wire up Caddy"
ssh "$HOST" "sed 's|YOUR_DOMAIN|$DOMAIN|' /tmp/todo-sync-caddy |
  (grep -q '$DOMAIN' /etc/caddy/Caddyfile || tee -a /etc/caddy/Caddyfile) &&
  systemctl reload caddy"

echo "==> done. Health check:"
curl -fsS "https://$DOMAIN/healthz"
echo
echo "The client sync key lives in $HOST:/etc/todo-sync.env (SYNC_KEY=...)."
