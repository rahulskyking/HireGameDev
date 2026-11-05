#!/usr/bin/env bash
# Daily encrypted PostgreSQL 16 backup with 30-day off-site S3/R2/B2 sync (M0.7)
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/hiregamedev}"
PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-hiregamedev}"
PGUSER="${PGUSER:-hiregamedev_backup}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
BACKUP_PASSPHRASE_FILE="${BACKUP_PASSPHRASE_FILE:-/etc/hiregamedev/backup.passphrase}"
RCLONE_REMOTE="${RCLONE_REMOTE:-}" # e.g. "r2:hiregamedev-backups/postgres"

umask 077
mkdir -p "${BACKUP_DIR}"

TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RAW_DUMP="${BACKUP_DIR}/${PGDATABASE}-${TIMESTAMP}.dump"
ENCRYPTED_DUMP="${RAW_DUMP}.gpg"

echo "[backup] Starting pg_dump for '${PGDATABASE}' at ${TIMESTAMP}..."
pg_dump \
  --host="${PGHOST}" \
  --port="${PGPORT}" \
  --username="${PGUSER}" \
  --format=custom \
  --compress=9 \
  --no-owner \
  --no-privileges \
  --file="${RAW_DUMP}" \
  "${PGDATABASE}"

if [[ ! -f "${BACKUP_PASSPHRASE_FILE}" ]]; then
  rm -f "${RAW_DUMP}"
  echo "[backup] ERROR: Missing encryption passphrase file at ${BACKUP_PASSPHRASE_FILE}" >&2
  exit 1
fi

echo "[backup] Encrypting dump with AES-256 (GPG symmetric)..."
gpg \
  --batch \
  --yes \
  --symmetric \
  --cipher-algo AES256 \
  --passphrase-file "${BACKUP_PASSPHRASE_FILE}" \
  --output "${ENCRYPTED_DUMP}" \
  "${RAW_DUMP}"

rm -f "${RAW_DUMP}"
sha256sum "${ENCRYPTED_DUMP}" > "${ENCRYPTED_DUMP}.sha256"

if [[ -n "${RCLONE_REMOTE}" ]]; then
  echo "[backup] Uploading encrypted backup to off-site remote '${RCLONE_REMOTE}'..."
  rclone copy "${ENCRYPTED_DUMP}" "${RCLONE_REMOTE}"
  rclone copy "${ENCRYPTED_DUMP}.sha256" "${RCLONE_REMOTE}"
  rclone delete "${RCLONE_REMOTE}" --min-age "${RETENTION_DAYS}d"
fi

echo "[backup] Pruning local backups older than ${RETENTION_DAYS} days..."
find "${BACKUP_DIR}" -maxdepth 1 -type f -name "${PGDATABASE}-*.dump.gpg*" -mtime "+${RETENTION_DAYS}" -delete

echo "[backup] Completed successfully: ${ENCRYPTED_DUMP}"
