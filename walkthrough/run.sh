#!/usr/bin/env bash
#
# The whole walk, from an empty database, in one command.
#
# All three steps are required and in this order, which is why they are a script rather than a note:
#
#   1. reset      - the walk creates every account and tender through the UI, so a second run against
#                   the first run's data hits "email already registered" and consumed invite tokens.
#   2. restart    - the bootstrap admin is created at start-up and its TOTP secret is generated once,
#                   on the run that creates it. The API must come up AFTER the database is empty.
#   3. walk       - drives the browser and writes screenshots/ and GUIDE.md.
#
# Running step 3 alone against yesterday's data is the mistake this script exists to prevent; it fails
# on an invite link that was already used, several acts after the actual cause.
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

./walkthrough/reset.sh

echo "==> restarting the API with demo seeding off"
# lsof can return SEVERAL pids (the dotnet launcher and the app it spawned), and passing that
# newline-separated list to kill as one argument fails with "arguments must be process or job IDs".
if lsof -ti:5080 >/dev/null 2>&1; then lsof -ti:5080 | xargs kill 2>/dev/null || true; sleep 3; fi
(
  cd src/backend/Api
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:5080 \
  DevSeed__Enabled=false \
  nohup dotnet run --no-launch-profile > /tmp/walkthrough-api.log 2>&1 &
)

printf '    waiting for the API'
for _ in $(seq 1 40); do
  if curl -s -m 2 -o /dev/null http://localhost:5080/health/ready; then printf ' up\n'; break; fi
  printf '.'; sleep 2
done

echo "==> walking"
node walkthrough/walk.mjs
