#!/usr/bin/env bash
# Restores one backup directory produced by backup.sh into a database.
#
# IT REFUSES TO RUN WITHOUT RESTORE_CONFIRM=yes. This overwrites whatever is in the target database,
# and the realistic accident is a restore aimed at production by a copied shell line, not by a
# decision. The confirmation has to be typed on the command that runs it.
#
# THE CHECKSUM IS VERIFIED FIRST. Restoring a truncated dump succeeds partway and leaves a database
# that looks populated, which is worse than a restore that refused.
#
# --clean --if-exists drops each object before recreating it, so an existing database is replaced
# rather than merged into. A merge would leave rows from two different points in time.
#
# --exit-on-error is not the default and matters: without it pg_restore reports errors, carries on,
# and exits 0, so a half-restored database reads as a successful restore.
#
# Uploaded documents are restored separately, with mc, because the object store is a second system
# and putting it behind the same confirmation would make a database-only restore impossible.
#
# What this can restore is exactly what backup.sh captured - see that file for what is not captured.

set -euo pipefail

BACKUP_DIRECTORY="${1:?usage: restore.sh <backup directory>}"
DATABASE_URL="${DATABASE_URL:?set DATABASE_URL to the database to restore into}"
RESTORE_CONFIRM="${RESTORE_CONFIRM:-no}"
MINIO_ALIAS="${MINIO_ALIAS:-}"
MINIO_BUCKET="${MINIO_BUCKET:-documents}"

if [ "${RESTORE_CONFIRM}" != "yes" ]; then
  echo "refusing to restore: re-run with RESTORE_CONFIRM=yes to overwrite ${DATABASE_URL%%\?*}" >&2
  exit 1
fi

dump="${BACKUP_DIRECTORY}/database.dump"
[ -f "${dump}" ] || { echo "no database.dump in ${BACKUP_DIRECTORY}" >&2; exit 1; }

echo "verifying ${dump}"
(cd "${BACKUP_DIRECTORY}" && (sha256sum -c database.dump.sha256 || shasum -a 256 -c database.dump.sha256))

echo "restoring into the target database"
pg_restore --dbname="${DATABASE_URL}" --clean --if-exists --exit-on-error "${dump}"

if [ -n "${MINIO_ALIAS}" ] && [ -d "${BACKUP_DIRECTORY}/documents" ]; then
  echo "restoring documents to ${MINIO_ALIAS}/${MINIO_BUCKET}"
  mc mirror --overwrite "${BACKUP_DIRECTORY}/documents" "${MINIO_ALIAS}/${MINIO_BUCKET}"
fi

echo "restore complete from ${BACKUP_DIRECTORY}"
