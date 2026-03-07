#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <duckdns_subdomain_without_suffix> <duckdns_token>"
  echo "Example: $0 necroforgeleaderboard 01234567-89ab-cdef-0123-456789abcdef"
  exit 1
fi

DOMAIN="$1"
TOKEN="$2"
DUCK_DIR="${HOME}/duckdns"
DUCK_SCRIPT="${DUCK_DIR}/duck.sh"
DUCK_LOG="${DUCK_DIR}/duck.log"
CRON_LINE="*/5 * * * * ${DUCK_SCRIPT} >/dev/null 2>&1"

mkdir -p "${DUCK_DIR}"

cat > "${DUCK_SCRIPT}" <<EOF
#!/usr/bin/env bash
set -euo pipefail
curl -fsS "https://www.duckdns.org/update?domains=${DOMAIN}&token=${TOKEN}&ip=" -o "${DUCK_LOG}"
EOF

chmod 700 "${DUCK_SCRIPT}"

"${DUCK_SCRIPT}"

# Ensure cron exists and is running.
if ! command -v crontab >/dev/null 2>&1; then
  echo "crontab command not found. Install cron first: sudo apt install -y cron"
  exit 1
fi

if command -v systemctl >/dev/null 2>&1; then
  sudo systemctl enable --now cron >/dev/null 2>&1 || true
fi

(
  crontab -l 2>/dev/null || true
  echo "${CRON_LINE}"
) | awk '!seen[$0]++' | crontab -

echo "DuckDNS installed."
echo "Domain: ${DOMAIN}.duckdns.org"
echo "Log file: ${DUCK_LOG}"
echo "Current response:"
cat "${DUCK_LOG}" || true
