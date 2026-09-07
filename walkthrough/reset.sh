#!/usr/bin/env bash
#
# Drops the database, re-runs migrations, and prints the new bootstrap admin secret.
#
# The walk is the seeding, which means it can only run against an empty database: a second run against
# data the first one created hits "email already registered", the registration silently fails, and the
# script then follows a spent verification token. That is exactly what happened on the first attempt,
# and the failure surfaced two screens later as "invalid email or password" - a long way from its cause.
#
# The API must be restarted after this: the bootstrap admin is created at start-up, and its TOTP secret
# is generated once, on the run that creates it.
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

echo "==> dropping and recreating the database"
# The API holds a connection pool open, so DROP is refused while it is running - and psql reports that
# as "being accessed by other users" and then, confusingly, "database already exists" from the CREATE
# that follows. Terminate the backends first; the API reconnects on its next request.
docker compose exec -T postgres psql -U postgres -c \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'mots_supplier_portal' AND pid <> pg_backend_pid();" >/dev/null
docker compose exec -T postgres psql -U postgres \
  -c "DROP DATABASE IF EXISTS mots_supplier_portal;" \
  -c "CREATE DATABASE mots_supplier_portal;" >/dev/null

echo "==> migrating (reference data, roles, permission claims)"
# dotnet-ef lives under $DOTNET_ROOT/tools and is NOT on a plain login PATH. Output used to be sent to
# /dev/null, so when the tool could not be found this step failed silently, the API then started
# against a database with no tables, and the first thing anybody saw was a crash in RoleSeeder several
# minutes later. Failures are shown, and a non-zero exit stops the script.
export PATH="$DOTNET_ROOT/tools:$PATH"
if ! dotnet ef database update --project src/backend/Infrastructure --startup-project src/backend/Api > /tmp/walkthrough-migrate.log 2>&1; then
  echo "    migration FAILED - last lines of /tmp/walkthrough-migrate.log:" >&2
  tail -5 /tmp/walkthrough-migrate.log >&2
  exit 1
fi

echo "==> clearing MailHog"
curl -s -X DELETE http://localhost:8025/api/v1/messages >/dev/null || true

echo
echo "Database is empty apart from reference data and roles."
echo "Now restart the API with DevSeed__Enabled=false. The bootstrap admin's authenticator secret is"
echo "generated on that start-up; walkthrough/totp.sh reads it from the database, so nothing needs"
echo "pasting anywhere."
