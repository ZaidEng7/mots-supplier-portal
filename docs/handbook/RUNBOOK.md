# Runbook: running the portal locally

> Every command below was executed against a clean database while writing this file. Where a step
> failed, the fix is in the step rather than in a footnote.

**Prerequisites:** Docker, .NET 10 SDK, **Node 22**. Where the SDK is not on `PATH`, every `dotnet`
command below is prefixed with `DOTNET_ROOT=$HOME/.dotnet`, the usual install location. Point it at a
different path if yours differs, or drop the prefix entirely if `dotnet` is already on your PATH.

> **Node 22, not 20.** This said "Node 20+" and Node 20 does not work: vitest, `tsc` and Playwright
> all fail at startup with `webidl.util.markAsUncloneable is not a function`, which reads as a broken
> dependency rather than as a wrong runtime. CI pins 22. There is no `.nvmrc` and no `engines` field
> to tell you, so the version you have is the version you get.

---

## 1. Services

```bash
docker compose up -d
```

Starts postgres (5432), MinIO (9000, console 9001), ClamAV (3310) and MailHog (SMTP 1025, web 8025).

ClamAV downloads its signature database on first run and takes a few minutes to report healthy.
Nothing in the start-up path waits on it; uploads are what need it.

```bash
docker compose ps --format "table {{.Service}}\t{{.Status}}"
```

## 2. Configuration

**Nothing to edit.** `appsettings.Development.json` is complete as committed, and deliberately holds
no connection string and no JWT key:

- the connection string falls back to the compose defaults (`RequiredConfiguration` enforces a real
  one outside Development)
- the JWT signing key is generated per process in Development; a persisted key is a production concern

## 3. Database and migrations

```bash
DOTNET_ROOT=$HOME/.dotnet dotnet ef database update --project src/backend/Infrastructure --startup-project src/backend/Api
```

Compose creates the `mots_supplier_portal` database. Migrations also seed reference data: 6
categories, 3 document types, 2 currencies, 7 units of measure, 4 regions, and the 8 roles with
their permission claims.

To start over:

```bash
docker compose exec -T postgres psql -U postgres -c "DROP DATABASE IF EXISTS mots_supplier_portal;" -c "CREATE DATABASE mots_supplier_portal;"
```

## 3a. The database role a deployment runs as

Local development connects as the owner, `postgres`, which is fine on a laptop and wrong anywhere else:
in PostgreSQL a table's owner bypasses `GRANT` and `REVOKE` on it, so the application would run every
request with rights to drop the schema.

A real deployment uses two connections. The **owner** runs migrations in the deploy step. The
**application** runs as a second role that can only read and write rows:

```bash
psql -v app_role=mots_app -v app_password="$APP_DB_PASSWORD" -v owner=postgres \
     -d mots_supplier_portal -f ops/sql/app-role.sql
```

Re-run it after every migration; it is idempotent, and the default privileges it sets mean new tables
are covered without a second visit.

Two settings go with it. The runtime connection string names `mots_app`, and `Hangfire:PrepareSchema`
is set to `false`, because preparing that schema is DDL the restricted role cannot issue. Run the deploy
step as the owner at least once so the Hangfire schema exists.

What the role may not do is asserted by `LeastPrivilegeRoleTests`, which runs the script above against a
real PostgreSQL and checks that `DROP`, `ALTER` and `CREATE` all come back as insufficient privilege.

## 3c. Deploying behind a reverse proxy

The documented architecture puts a proxy in front for TLS and `/api` routing. Name it, or the
application will treat the proxy as the client on every request:

```bash
Network__TrustedProxies__0=10.0.0.4          # one or more proxy addresses
Network__TrustedProxyNetworks__0=10.0.0.0/8  # or the network they come from
```

Five things read the client address - the two rate-limit partitions, the address recorded against a
sign-in and a refresh, and the address stamped on every audited action. Unset, all five see the proxy:
the ten-per-minute limit becomes one bucket shared by everyone, and the audit trail records one
address for every actor.

Setting it wrong is worse than leaving it unset, so it is deliberately not guessed. `X-Forwarded-For`
can be sent by anyone; it is honoured only on a connection arriving from an address named above, and
only the last hop is read. Where nothing is named the middleware is never added and the socket
address stands, which is correct for local development and for a deployment with nothing in front.

`ForwardedClientAddressTests` holds both halves: a spoofed header from an untrusted connection is
ignored, and a header from the proxy partitions the limit.

## 3b. Backups

```bash
export DATABASE_URL='postgresql://user:password@host:5432/mots_supplier_portal'
export BACKUP_DESTINATION=/var/backups/mots
export MINIO_ALIAS=production
ops/backup/backup.sh
```

Each run writes a timestamped directory holding `database.dump`, its checksum, and `documents/`.
Without `MINIO_ALIAS` the uploaded documents are not in the backup, and the script says so on stderr.

Restoring names a run directory and has to be confirmed on the command line:

```bash
RESTORE_CONFIRM=yes ops/backup/restore.sh /var/backups/mots/2026-09-15T02-00-00Z
```

Schedule `backup.sh` with cron or a systemd timer, and make its failures visible somewhere a person
reads. The recovery point is however often it runs; reaching the documented 15 minutes needs WAL
archiving, which is not set up. `ops/backup/README.md` has the rest, including what is still owed.

## 4. The API

```bash
cd src/backend/Api && DOTNET_ROOT=$HOME/.dotnet ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --no-launch-profile
```

**`ASPNETCORE_URLS` is required and this is the trap.** Three ports disagree in the repository:
`launchSettings.json` says 5107, a bare `dotnet run --no-launch-profile` gives Kestrel's default
5000, and the SPA hard-defaults to `http://localhost:5080` (`src/frontend/src/api/auth.ts`). Without
pinning 5080 the SPA loads and every request fails against a port nothing is listening on.

On the run that creates them, the seed credentials are printed once:

```
[dev-seed] system_admin created: admin@mots.local / motsdemo2026
[dev-seed] TOTP secret (add to an authenticator app): <generated, different every fresh database>
[dev-seed] demo personas: officer@ manager@ evaluator@ ministry@ supplier@ supplier.user@mots.local / motsdemo2026
```

Health: `curl http://localhost:5080/health/ready`, postgres, migrations and object storage.

## 5. The SPA

```bash
cd src/frontend && npm install && npm run dev
```

- **SPA, http://localhost:5173** — this is the one to open
- API http://localhost:5080 · MailHog http://localhost:8025 · MinIO console http://localhost:9001 (minioadmin / minioadmin)

## 6. Accounts

Seeded automatically at start-up in Development by `DevDataSeeder`. Idempotent: restarting does not
duplicate anything.

One password across all nine, so a walkthrough never stops to look up which account is the exception.
The two that used to be exceptions (the onboarding reviewer and the bootstrap admin) are the two a
walk cannot get past without. Production is unaffected: it supplies `DevSeed:AdminPassword` and never
reaches the fallback, and system_admin still requires a TOTP code, which is what actually guards it.

| Persona | Email | Password |
|---|---|---|
| supplier_admin | `supplier@mots.local` | `motsdemo2026` |
| supplier_user | `supplier.user@mots.local` | `motsdemo2026` |
| procurement_officer | `officer@mots.local` | `motsdemo2026` |
| procurement_manager | `manager@mots.local` | `motsdemo2026` |
| evaluator | `evaluator@mots.local` | `motsdemo2026` |
| ministry_viewer | `ministry@mots.local` | `motsdemo2026` |
| onboarding_reviewer | `reviewer@mots.local` | `motsdemo2026` |
| system_admin | `admin@mots.local` | `motsdemo2026` + **TOTP** |
| procurement_manager (second) | `manager2@mots.local` | `motsdemo2026` |

**system_admin needs a TOTP code**: it is the only role in `Mfa:RequiredRoles`. Add the printed
secret to an authenticator app, or generate a code:

```bash
python3 -c "import hmac,hashlib,struct,time,base64;s='PASTE_SECRET_HERE';k=base64.b32decode(s+'='*(-len(s)%8));h=hmac.new(k,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest();o=h[19]&0xf;print('%06d'%((struct.unpack('>I',h[o:o+4])[0]&0x7fffffff)%1000000))"
```

The secret is regenerated on every fresh database, so take it from your own start-up log.

**`ministry_viewer` has no organization, on purpose.** BRULE-086 grants the Ministry
cross-organization aggregate access, so pinning it to one buying body would be a narrower grant
wearing the same name. The visible consequence is that the org-scoped procurement report answers 404
for that persona.

## 7. Seeded data

Enough that no screen renders an empty state for want of a row:

| Reference | Title | State | Carries |
|---|---|---|---|
| `RFQ-DEMO-0001` | Catering supplies | Draft | editable items and requirements |
| `RFQ-DEMO-0002` | Cleaning services | InternalReview | a manager's approval decision |
| `RFQ-DEMO-0003` | Office furniture | Approved | ready to publish |
| `RFQ-DEMO-0004` | Kitchen equipment | SubmissionOpen | `PRP-DEMO-0001`, draft |
| `RFQ-DEMO-0005` | Lift maintenance | UnderEvaluation | `PRP-DEMO-0002` submitted · evaluation part-scored · award **Recommended** |
| `RFQ-DEMO-0006` | Stationery | Clarification | `PRP-DEMO-0003`, ClarificationRequested |

Plus **5 suppliers**: `SUP-DEMO-0001`..`0005`, at Approved/Active, Submitted, UnderReview,
Approved/Suspended and ProfileInProgress, and **one evaluation** on `RFQ-DEMO-0005`, assigned,
opened and part-scored.

**It stops short of any verdict.** Nothing is consolidated, and the award on `RFQ-DEMO-0005` is
Recommended and neither routed nor approved. Consolidation ranks bids and an approved award names a
winner: both are outcomes, and a fixture that invents one puts a tender result in the database that
nobody decided. So the manager's approval queue has a real row to work and no tender here has a
winner. Drive those through the UI.

## 8. Tests

```bash
DOTNET_ROOT=$HOME/.dotnet dotnet test src/backend/Tests/Unit
DOTNET_ROOT=$HOME/.dotnet dotnet test src/backend/Tests/Architecture
DOTNET_ROOT=$HOME/.dotnet dotnet test src/backend/Tests/Integration   # Testcontainers; ~10 min
cd src/frontend && npm run typecheck && npx vitest run && npx playwright test
```

The integration suite starts its own Postgres through Testcontainers and does not touch the database
above.

## 9. Things that look broken and are not

- **A fresh registration lands on "Your application is under review", not a dashboard.** Correct: a
  new supplier is in Draft. The seeded `supplier@mots.local` is already Active and does get one.
- **The admin overview's "No real ERP integration is configured" is about the outbox only.** It sits on
  the Outbox card. The outbox still drains to a log line, which is T-089 stating a vacuum rather than a
  failure. Awards still go to `StubErpPurchaseOrderAdapter`, which sends no purchase order. The supplier
  import is connected: it reads the ministry's ERP. It runs from **Supplier import**
  (`/back-office/erp-import`) and every hour by itself, once an ERP connection is configured, either on
  **Connected systems** or in the `Erp` settings. Without one, the screen answers that the connection is
  not configured, and the hourly job skips quietly. The push that creates approved suppliers in the ERP
  does not use the outbox, and it sends nothing until somebody switches it on (§11). See
  `ERP-IMPORT.md`.
- **The evaluation is part-scored and not consolidated**, so comparison and award screens show a
  position rather than a result. See §7.
- **`report.read` reaches `procurement_manager` and `ministry_viewer` only.** An officer has no
  Reports link, by grant, not by accident.
- **Recurring jobs are enabled** — 8 of them, including one that opens and closes submission windows,
  so RFQ states move on their own while you watch, the hourly ERP supplier sync, and the five-minute
  sweep of the ERP supplier push, which does nothing while its switch is off.

## 10. Three screens have no link to them

Not a "looks broken and is not": these are genuinely unreachable by clicking, and the only way to
open them today is to type the address:

| Screen | Address | Who holds the permission |
|---|---|---|
| Procurement dashboard (SCR-400) | `/back-office/procurement` | procurement_officer, procurement_manager |
| Reviewer dashboard (SCR-300) | `/back-office/review-dashboard` | onboarding_reviewer |
| Reports (FEAT-19.1/19.2) | `/back-office/reports` | procurement_manager, ministry_viewer |

`/back-office/procurement/approvals` **is** linked, from the procurement dashboard, which is itself
unlinked, so the manager's approval queue sits behind a page nobody can navigate to.

`/back-office/dashboard` is the landing every back-office persona gets, and it is a placeholder that
lists the permissions on your token. The three real dashboards are the ones above.

## 11. Switching on the ERP supplier push

The push creates in the ERP each supplier a reviewer approves, and it is off until somebody turns it on.
`ERP-IMPORT.md` §8 explains it. Unlike the rest of this file, these steps have not been run yet: the
push has not been switched on anywhere.

Two kinds of approved supplier do not go. **A supplier approved before the `ErpSupplierPush` migration
is not pushed.** The migration gives every existing supplier `NotRequested`, and only an approval asks
for a push. No screen or route can ask for one: **Retry** works only on a failed push, and a new
approval comes only after a compliance-critical edit sends the supplier back to review. The waiting
count in step 5 leaves these suppliers out. **A supplier out of service is not pushed while it stays out**: one suspended by a person
or by the document-expiry job, or deactivated. It goes once it is back in service, and a deactivated one
never does. The import's own hold while the ERP approves a pushed supplier does not count as out of
service.

1. **Try it on the ERP's test server first**, on a copy of the registry with the schedules off, as
   `ERP-IMPORT.md` §8.9 describes. Never point the registry's own connection at the test server: the
   hourly import would read the test server's suppliers into the registry.
2. **Back up, then apply the `ErpSupplierPush` migration** with §3's command. The API does not migrate
   at startup. The migration stops, naming them, if two suppliers share an ERP identifier. The switch
   starts off. Then name the ERP server in `Erp:WriteHosts` in the deployment's configuration, as a host
   or a host and port. The list is empty by default, and an empty list refuses every write whatever the
   switch says.
3. **Agree the ERP user's rights with the ERP colleague.** **Test connection** proves the import's reads
   only. The push also creates Supplier, Address, Contact and User records, reads all four, updates
   Supplier (its portal users) and Contact, reads the field lists of Supplier, Address and Contact
   through `frappe.desk.form.load.getdoctype`, and asks `frappe.auth.get_logged_user` for its own user
   name, which the hourly import also asks, to leave the push's own creates to the push. The group
   list on Connected systems reads Supplier Group. A refused credential stops every run without marking
   any supplier, so the API log is the only place it shows.
4. **Leave the schedules on.** The push runs straight after each approval either way, but a push that
   failed for a passing reason is tried again only by the sweep, which `Jobs:EnableRecurring=false`
   removes.
5. **Choose the group and switch on.** On **Connected systems**, on the ERP's card, choose the default
   supplier group from the ERP's list and tick **Create approved suppliers in the ERP**. The question
   says how many approved suppliers in service are waiting, read afresh as it opens. They go to the ERP
   within minutes, five at each five-minute sweep, and nothing here can take one back, so cancel if the
   number is not the one you expect. Otherwise press **Turn on and save**. A large number does not trip
   the import's limit on suspensions: a pushed supplier seen for the first time in Draft is left out of
   that count.
6. **Watch the first one with the ERP colleague.** Within seconds of its approval, the supplier's review
   page should say **ERP: created** with the ERP's name for it. Its audit trail says what was made and
   which values the ERP had no field for. At the next hourly import the supplier shows as suspended,
   because the ERP holds it in Draft, however many were pushed at once. That is expected: the first
   import after the ERP approves it releases it.
7. **If nothing happens**, read the API log for lines that start "Supplier push to the ERP". "skipped"
   means the connection or the switch is off, no group is set, or an import or another push held the
   lock. "stopped" means a refused credential, or the connection or the switch turned off during a run.
   A supplier whose chip says **ERP: waiting** and never moves may be out of service: check its state.
   If the page says **ERP: failed**, it shows why. Fix the cause, then press **Retry**, which needs
   `admin.integrations.manage`, held by default only by the system administrator. Retry refuses a
   supplier out of service.
8. **If Run the import answers that an import or a supplier push is running**, wait a minute or two: a
   push holds the import's lock while it works. The hourly import, finding the lock taken, logs "Hourly
   ERP supplier sync waits" and tries once more two minutes later. Only if the lock is still taken then
   does it skip that hour.

**Switching it off** is unticking the switch and pressing **Save**. Nothing is sent from then on.
Suppliers approved meanwhile wait, with no attempt counted, and go when it is turned on again. Suppliers
already created in the ERP stay there; removing one is the ERP team's work.

**Moving the connection to another ERP**: switch off first, then change the address, choose a group from
the new ERP's list, and switch on again. The saved group is a name on the old ERP, and does not follow
the address.
