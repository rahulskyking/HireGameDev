#!/usr/bin/env bash
# Automated PostgreSQL 16 backup decrypt + scratch-DB restore verification drill (M0.7)
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 /path/to/hiregamedev-YYYYMMDDTHHMMSSZ.dump.gpg" >&2
  exit 1
fi

ENCRYPTED_DUMP="$1"
PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-postgres}"
BACKUP_PASSPHRASE_FILE="${BACKUP_PASSPHRASE_FILE:-/etc/hiregamedev/backup.passphrase}"

if [[ ! -f "${ENCRYPTED_DUMP}" ]]; then
  echo "[restore-test] ERROR: Backup file not found: ${ENCRYPTED_DUMP}" >&2
  exit 1
fi

if [[ -f "${ENCRYPTED_DUMP}.sha256" ]]; then
  echo "[restore-test] Verifying SHA-256 checksum..."
  (cd "$(dirname "${ENCRYPTED_DUMP}")" && sha256sum --check "$(basename "${ENCRYPTED_DUMP}.sha256")")
fi

SCRATCH_DB="hgd_restore_verify_$(date -u +%Y%m%d_%H%M%S)"
TMP_DUMP="$(mktemp /tmp/hgd-restore-XXXXXX.dump)"

cleanup() {
  rm -f "${TMP_DUMP}"
  dropdb --if-exists --host="${PGHOST}" --port="${PGPORT}" --username="${PGUSER}" "${SCRATCH_DB}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "[restore-test] Decrypting ${ENCRYPTED_DUMP}..."
gpg \
  --batch \
  --yes \
  --decrypt \
  --passphrase-file "${BACKUP_PASSPHRASE_FILE}" \
  --output "${TMP_DUMP}" \
  "${ENCRYPTED_DUMP}"

echo "[restore-test] Creating scratch database '${SCRATCH_DB}'..."
createdb --host="${PGHOST}" --port="${PGPORT}" --username="${PGUSER}" "${SCRATCH_DB}"

echo "[restore-test] Running pg_restore into '${SCRATCH_DB}'..."
pg_restore \
  --host="${PGHOST}" \
  --port="${PGPORT}" \
  --username="${PGUSER}" \
  --dbname="${SCRATCH_DB}" \
  --no-owner \
  --no-privileges \
  --exit-on-error \
  "${TMP_DUMP}"

echo "[restore-test] Auditing core Phase 0 tables..."
psql --host="${PGHOST}" --port="${PGPORT}" --username="${PGUSER}" --dbname="${SCRATCH_DB}" -v ON_ERROR_STOP=1 <<'SQL'
SELECT
  (SELECT COUNT(*) FROM "__EFMigrationsHistory") AS applied_migrations,
  (SELECT COUNT(*) FROM roles)                   AS roles_count,
  (SELECT COUNT(*) FROM cities)                  AS cities_count,
  (SELECT COUNT(*) FROM skills)                  AS skills_count,
  (SELECT COUNT(*) FROM app_settings)            AS settings_count;
SQL

echo "[restore-test] Restore verification PASSED; dropping scratch DB '${SCRATCH_DB}'."
