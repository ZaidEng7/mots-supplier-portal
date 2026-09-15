#!/usr/bin/env bash
# Takes one backup: a custom-format database dump, and a mirror of the document bucket beside it.
#
# WHAT A RUN PRODUCES
#
# <destination>/<UTC timestamp>/database.dump, its .sha256, and documents/ holding the object store's
# contents. Nothing else writes into that directory, so a whole run is one unit to copy off-site,
# verify, or delete.
#
# WHY CUSTOM FORMAT AND NOT PLAIN SQL
#
# pg_restore can select, reorder and parallelise out of a custom-format dump, and it is the format
# restore.sh expects. A plain-SQL dump can only be replayed start to finish.
#
# THE CHECKSUM IS WRITTEN AT BACKUP TIME ON PURPOSE. A dump silently truncated by a full disk is a
# valid file, and the only moment its correct length is known is the moment it was written.
#
# RETENTION DELETES, SO IT IS THE ONE THING HERE THAT CAN LOSE DATA. It only ever removes whole run
# directories under the destination whose names match the timestamp pattern this script writes, and
# it runs after a successful dump, never before - a failed run must not be able to prune the last
# good one. BACKUP_KEEP=0 disables it.
#
# WHAT THIS IS NOT
#
# Not point-in-time recovery. Running this hourly gives a recovery point of one hour; the 15-minute
# target in the requirements needs continuous WAL archiving, which is infrastructure rather than a
# script. Not off-site: the destination is wherever this is pointed, and a backup on the same host
# as the database survives exactly the failures that do not matter. Not encrypted at rest. Those
# three are what ops/backup/README.md still lists as owed.
#
# The restore side is proven on every push rather than documented: see BackupRestoreDrillTests.

set -euo pipefail

DATABASE_URL="${DATABASE_URL:?set DATABASE_URL to the database to back up}"
BACKUP_DESTINATION="${BACKUP_DESTINATION:-./backups}"
BACKUP_KEEP="${BACKUP_KEEP:-14}"
MINIO_ALIAS="${MINIO_ALIAS:-}"
MINIO_BUCKET="${MINIO_BUCKET:-documents}"

run="$(date -u +%Y-%m-%dT%H-%M-%SZ)"
target="${BACKUP_DESTINATION}/${run}"
mkdir -p "${target}"

echo "backing up database to ${target}/database.dump"
pg_dump --dbname="${DATABASE_URL}" --format=custom --file="${target}/database.dump"

(cd "${target}" && (sha256sum database.dump || shasum -a 256 database.dump) > database.dump.sha256)

if [ -n "${MINIO_ALIAS}" ]; then
  echo "mirroring ${MINIO_ALIAS}/${MINIO_BUCKET} to ${target}/documents"
  mc mirror --overwrite "${MINIO_ALIAS}/${MINIO_BUCKET}" "${target}/documents"
else
  echo "MINIO_ALIAS is unset, so uploaded documents are NOT in this backup." >&2
fi

if [ "${BACKUP_KEEP}" -gt 0 ]; then
  runs="$(find "${BACKUP_DESTINATION}" -mindepth 1 -maxdepth 1 -type d \
    -name '????-??-??T??-??-??Z' | sort)"
  total="$(printf '%s\n' "${runs}" | grep -c . || true)"
  surplus=$((total - BACKUP_KEEP))
  if [ "${surplus}" -gt 0 ]; then
    printf '%s\n' "${runs}" | head -n "${surplus}" | while read -r old; do
      echo "pruning ${old}"
      rm -rf "${old}"
    done
  fi
fi

echo "backup complete: ${target}"
