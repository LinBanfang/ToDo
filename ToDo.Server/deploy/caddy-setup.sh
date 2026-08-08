#!/usr/bin/env bash
# Idempotently wires the To Do sync site into the VPS Caddyfile, then reloads Caddy.
#
# Usage:  sudo bash caddy-setup.sh <DOMAIN> <PORT>
#
# Replaces any existing site block whose opening line belongs to <DOMAIN> (e.g. an
# old "domain { ... }" block bound to :443) with a fresh "<DOMAIN>:<PORT>" block that
# reverse-proxies to the sync server, so re-runs never leave duplicate blocks behind.
# Runs `caddy fmt` before reloading. Deployed via scp by deploy.sh.
set -euo pipefail

DOMAIN="${1:?usage: caddy-setup.sh DOMAIN PORT}"
PORT="${2:?usage: caddy-setup.sh DOMAIN PORT}"
CADDYFILE="/etc/caddy/Caddyfile"
SITE="$DOMAIN:$PORT"

# Normalize any CRLF leftovers from old appends before parsing (caddy fmt also does
# this, but we need it before awk reads the file).
sed -i 's/\r$//' "$CADDYFILE"

# Drop any existing block for this domain, then append a fresh one. awk uses only
# portable string ops (index/substr) — regex EREs behave differently across the
# minimal awk on the VPS vs gawk/mawk, so we avoid them entirely. A block-opening
# line is one whose first token starts with DOMAIN and whose last char is "{" —
# e.g. "DOMAIN {" or "DOMAIN:8443 {". Comment lines are left untouched.
tmp="$(mktemp)"
awk -v d="$DOMAIN" '
  index($1, d) == 1 && substr($0, length($0), 1) == "{" { skip = 1 }
  skip && $0 == "}" { skip = 0; next }
  skip { next }
  { print }
' "$CADDYFILE" > "$tmp"
cp "$tmp" "$CADDYFILE"
rm -f "$tmp"

printf '%s {\n    reverse_proxy 127.0.0.1:5080\n}\n' "$SITE" >> "$CADDYFILE"

caddy fmt --overwrite "$CADDYFILE"
systemctl reload caddy
