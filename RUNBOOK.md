# Runbook — running the portal locally

> Every command below was executed against a clean database while writing this file. Where a step
> failed, the fix is in the step rather than in a footnote.

**Prerequisites:** Docker, .NET 10 SDK, **Node 22**. On this machine the SDK is not on `PATH`, so every
`dotnet` command is prefixed with `DOTNET_ROOT=/Users/zaid/.dotnet` — drop that prefix if yours is.

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
Nothing in the start-up path waits on it — uploads are what need it.

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
DOTNET_ROOT=/Users/zaid/.dotnet dotnet ef database update --project src/backend/Infrastructure --startup-project src/backend/Api
```

Compose creates the `mots_supplier_portal` database. Migrations also seed reference data — 6
categories, 3 document types, 2 currencies, 7 units of measure, 4 regions — and the 8 roles with
their permission claims.

To start over:

```bash
docker compose exec -T postgres psql -U postgres -c "DROP DATABASE IF EXISTS mots_supplier_portal;" -c "CREATE DATABASE mots_supplier_portal;"
```

## 4. The API

```bash
cd src/backend/Api && DOTNET_ROOT=/Users/zaid/.dotnet ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --no-launch-profile
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

Health: `curl http://localhost:5080/health/ready` — postgres, migrations and object storage.

## 5. The SPA

```bash
cd src/frontend && npm install && npm run dev
```

- **SPA — http://localhost:5173** — this is the one to open
- API http://localhost:5080 · MailHog http://localhost:8025 · MinIO console http://localhost:9001 (minioadmin / minioadmin)

## 6. Accounts

Seeded automatically at start-up in Development by `DevDataSeeder`. Idempotent — restarting does not
duplicate anything.

One password across all nine, so a walkthrough never stops to look up which account is the exception.
The two that used to be exceptions — the onboarding reviewer and the bootstrap admin — are the two a
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

**system_admin needs a TOTP code** — it is the only role in `Mfa:RequiredRoles`. Add the printed
secret to an authenticator app, or generate a code:

```bash
python3 -c "import hmac,hashlib,struct,time,base64;s='PASTE_SECRET_HERE';k=base64.b32decode(s+'='*(-len(s)%8));h=hmac.new(k,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest();o=h[19]&0xf;print('%06d'%((struct.unpack('>I',h[o:o+4])[0]&0x7fffffff)%1000000))"
```

The secret is regenerated on every fresh database — take it from your own start-up log.

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

Plus **5 suppliers** — `SUP-DEMO-0001`..`0005`, at Approved/Active, Submitted, UnderReview,
Approved/Suspended and ProfileInProgress — and **one evaluation** on `RFQ-DEMO-0005`, assigned,
opened and part-scored.

**It stops short of any verdict.** Nothing is consolidated, and the award on `RFQ-DEMO-0005` is
Recommended and neither routed nor approved. Consolidation ranks bids and an approved award names a
winner: both are outcomes, and a fixture that invents one puts a tender result in the database that
nobody decided. So the manager's approval queue has a real row to work and no tender here has a
winner. Drive those through the UI.

## 8. Tests

```bash
DOTNET_ROOT=/Users/zaid/.dotnet dotnet test src/backend/Tests/Unit
DOTNET_ROOT=/Users/zaid/.dotnet dotnet test src/backend/Tests/Architecture
DOTNET_ROOT=/Users/zaid/.dotnet dotnet test src/backend/Tests/Integration   # Testcontainers; ~10 min
cd src/frontend && npm run typecheck && npx vitest run && npx playwright test
```

The integration suite starts its own Postgres through Testcontainers and does not touch the database
above.

## 9. Things that look broken and are not

- **A fresh registration lands on "Your application is under review", not a dashboard.** Correct — a
  new supplier is in Draft. The seeded `supplier@mots.local` is already Active and does get one.
- **No real ERP integration is configured.** The admin overview says so. The outbox drains to a log
  line; that is T-089 stating a vacuum rather than a failure.
- **The evaluation is part-scored and not consolidated**, so comparison and award screens show a
  position rather than a result. See §7.
- **`report.read` reaches `procurement_manager` and `ministry_viewer` only.** An officer has no
  Reports link, by grant, not by accident.
- **Recurring jobs are enabled** — 6 of them, including one that opens and closes submission windows,
  so RFQ states move on their own while you watch.

## 10. Three screens have no link to them

Not a "looks broken and is not" — these are genuinely unreachable by clicking, and the only way to
open them today is to type the address:

| Screen | Address | Who holds the permission |
|---|---|---|
| Procurement dashboard (SCR-400) | `/back-office/procurement` | procurement_officer, procurement_manager |
| Reviewer dashboard (SCR-300) | `/back-office/review-dashboard` | onboarding_reviewer |
| Reports (FEAT-19.1/19.2) | `/back-office/reports` | procurement_manager, ministry_viewer |

`/back-office/procurement/approvals` **is** linked — from the procurement dashboard, which is itself
unlinked, so the manager's approval queue sits behind a page nobody can navigate to.

`/back-office/dashboard` is the landing every back-office persona gets, and it is a placeholder that
lists the permissions on your token. The three real dashboards are the ones above.
