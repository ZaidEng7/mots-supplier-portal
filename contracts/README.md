# API contract baseline

`openapi-v1.baseline.json` is the committed shape of `/api/v1`, and CI compares the document the build
produces against it.

## What the gate does and does not do

It fails on a **breaking** diff — a removed path or operation, a removed or newly-required property, a
narrowed type, a removed enum value, a changed response code. Additive changes pass, because
`API-ARCHITECTURE.md` §Versioning says they are non-breaking and ship within the current version.

It does **not** check the four documentation requirements §11 also lists: every endpoint naming its
permission, its error `type`s, its pagination mode, and whether it needs `Idempotency-Key`/`If-Match`. That
half is a sweep across 228 operations, not a switch, and turning on a style gate that fails on day one for
all of them would produce 228 hurried annotations rather than accurate ones. It is recorded as the
remaining half in `COMPLETION-INVENTORY.md` §3.4 rather than half-done here.

## Updating the baseline

A deliberate breaking change is a **version** decision, not a baseline edit: §Versioning lists what
requires `/api/v2`. For an additive change the baseline can simply be refreshed:

```bash
# with the API running in Development
curl -s http://localhost:5080/openapi/v1.json \
  | python3 -c "import json,sys; print(json.dumps(json.load(sys.stdin), indent=2, sort_keys=True, ensure_ascii=False))" \
  > contracts/openapi-v1.baseline.json
```

Formatted with sorted keys on purpose: without it the diff is a key-order shuffle and the gate reports
noise, which is how a gate stops being read.

## One thing found while building this

`/openapi/v1.json` had never been reachable. `MapOpenApi()` states no authorization, so NFR-SEC-004's
deny-by-default `FallbackPolicy` closed it — the document was generated and served to nobody, including the
SPA type generation and the ERP client §11 names as its consumers. It answers 200 in Development now, and
requires `admin.users.manage` elsewhere.
