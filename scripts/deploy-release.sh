#!/usr/bin/env bash
# Atomic release deployment helper (backup -> efbundle -> symlink swap -> health check)
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 /path/to/web-publish-dir /path/to/efbundle" >&2
  exit 1
fi

WEB_ARTIFACT_DIR="$1"
EFBUNDLE_PATH="$2"
APP_ROOT="${APP_ROOT:-/opt/hiregamedev}"
ENV_FILE="${ENV_FILE:-/etc/hiregamedev/hiregamedev.env}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:5000/health}"

if [[ ! -d "${WEB_ARTIFACT_DIR}" || ! -f "${EFBUNDLE_PATH}" ]]; then
  echo "[deploy] ERROR: Missing publish directory or efbundle binary." >&2
  exit 1
fi

RELEASE_ID="$(date -u +%Y%m%dT%H%M%SZ)"
RELEASE_DIR="${APP_ROOT}/releases/${RELEASE_ID}"

echo "[deploy] Staging release ${RELEASE_ID}..."
mkdir -p "${RELEASE_DIR}"
cp -a "${WEB_ARTIFACT_DIR}/." "${RELEASE_DIR}/"
chmod +x "${EFBUNDLE_PATH}"

if [[ -x "$(dirname "$0")/backup-db.sh" && "${SKIP_PREDEPLOY_BACKUP:-0}" != "1" ]]; then
  echo "[deploy] Taking pre-migration database backup..."
  "$(dirname "$0")/backup-db.sh"
fi

echo "[deploy] Applying EF Core migrations via efbundle..."
(
  set -a
  # shellcheck disable=SC1090
  source "${ENV_FILE}"
  set +a
  "${EFBUNDLE_PATH}" --connection "${ConnectionStrings__DefaultConnection}"
)

echo "[deploy] Updating ${APP_ROOT}/current symlink and restarting hiregamedev-web..."
ln -sfn "${RELEASE_DIR}" "${APP_ROOT}/current"
systemctl restart hiregamedev-web.service

echo "[deploy] Polling ${HEALTH_URL}..."
for attempt in {1..15}; do
  if curl --fail --silent --max-time 3 "${HEALTH_URL}" >/dev/null; then
    echo "[deploy] Release ${RELEASE_ID} is healthy."
    exit 0
  fi
  sleep 2
done

echo "[deploy] ERROR: Health check failed after 30s. Check 'journalctl -u hiregamedev-web -n 100'." >&2
exit 1
