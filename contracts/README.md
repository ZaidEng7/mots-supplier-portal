# API contract baseline

`openapi-v1.baseline.json` is the committed shape of `/api/v1`, and CI compares the document the build
produces against it.

## What the gate does and does not do

It fails on a **breaking** diff — a removed path or operation, a removed or newly-required property, a
narrowed type, a removed enum value, a changed response code. Additive changes pass, because
`API-ARCHITECTURE.md` §Versioning says they are non-breaking and ship within the current version.

It does **not** check three of the four documentation requirements §11 also lists: every endpoint naming its
permission, its error `type`s and its pagination mode. That half is a sweep across 228 operations, not a
switch, and turning on a style gate that fails on day one for all of them would produce 228 hurried
annotations rather than accurate ones. It is recorded as the remaining half in `COMPLETION-INVENTORY.md` §3.4
rather than half-done here.

The fourth — **whether an operation needs `If-Match`** — is now in the document, and was not annotated by
anybody: `RequireIfMatch()` attaches endpoint metadata and an OpenAPI operation transformer turns it into a
required `If-Match` header parameter with 412 and 428, alongside an `ETag` response header on every read that
issues one. Derived from the filter that enforces it, so the two cannot disagree. The SPA's own sweep
(`src/frontend/src/api/preconditionCoverage.test.ts`) reads this file to learn which of its writes need a
version; `IfMatchPreconditionSweepTests` fails if a guarded route is missing from the baseline, so a stale
baseline cannot quietly switch that check off.

## Updating the baseline

A deliberate breaking change is a **version** decision, not a baseline edit: §Versioning lists what
requires `/api/v2`. For an additive change the baseline can simply be refreshed:

```bash
DOTNET_ROOT=$HOME/.dotnet UPDATE_OPENAPI_BASELINE=1 dotnet test \
  src/backend/Tests/Integration/MotsSupplierPortal.Tests.Integration.csproj \
  --filter OpenApiContractTests
```

The same idiom `PERMISSIONS.md` uses for the generated permission catalogue, and it writes nothing unless
the variable is set. This replaced a curl pipeline that needed the API running in Development with a
database and a port — true, and it meant refreshing the contract was a small chore, so it was done rarely
and the baseline drifted behind additive changes. The integration fixture already has the database.

Still formatted with sorted keys: without it the diff is a key-order shuffle and the gate reports noise,
which is how a gate stops being read.

## One thing found while building this

`/openapi/v1.json` had never been reachable. `MapOpenApi()` states no authorization, so NFR-SEC-004's
deny-by-default `FallbackPolicy` closed it — the document was generated and served to nobody, including the
SPA type generation and the ERP client §11 names as its consumers. It answers 200 in Development now, and
requires `admin.users.manage` elsewhere.
