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
dotnet ef database update --project src/backend/Infrastructure --startup-project src/backend/Api >/dev/null 2>&1

echo "==> clearing MailHog"
curl -s -X DELETE http://localhost:8025/api/v1/messages >/dev/null || true

echo
echo "Database is empty apart from reference data and roles."
echo "Now restart the API with DevSeed__Enabled=false, take the TOTP secret from its log,"
echo "and put it in walkthrough/walk.mjs (TOTP_SECRET)."
