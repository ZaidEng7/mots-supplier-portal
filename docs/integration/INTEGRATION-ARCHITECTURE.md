# Integration Architecture — Patterns, Contracts & Resilience

> **Status:** Baseline v1 · **Owner:** Principal Architect · **Date:** 2026-08-26
> Canonical parents: [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md)
> Sibling: [`ERP-INTEGRATION-BOUNDARY.md`](./ERP-INTEGRATION-BOUNDARY.md)

This document specifies **how** the portal integrates with ERPNext per flow: the mechanism chosen and
why, the wire contracts (DTOs), idempotency, retry/backoff, dead-lettering, reconciliation, the
sync-status model, logging/monitoring, and failure/degraded-mode behavior. It builds directly on the
ownership split in the [boundary doc](./ERP-INTEGRATION-BOUNDARY.md).

Foundational tech in play (from
[decisions §2](../architecture/00-foundational-decisions.md#2-technology-decisions--justifications)):
**.NET 10 / ASP.NET Core**, **Hangfire (Postgres)** for scheduled/recurring jobs and Outbox dispatch,
**Serilog + OpenTelemetry** for telemetry, **EF Core 10 / PostgreSQL 17** for the Outbox and sync
state, **Mapperly** for the ACL translators.

---

## 1. Integration flows at a glance

| # | Flow | Direction | Trigger | Mechanism | Why this mechanism |
|---|---|---|---|---|---|
| F1 | **Supplier master sync** (approval → ERP `Supplier` create/update) | Portal → ERP | `SupplierApproved` / post-approval edit to ERP-owned field | **Transactional Outbox → async publish (REST POST/PUT)** | Write must survive ERP downtime; must never block the approval click. Outbox gives at-least-once + ordering per aggregate. |
| F2 | **Award → Purchase Order** | Portal → ERP | `AwardCreated` (award finalized) | **Transactional Outbox → async publish (REST POST)** | Financial commitment; must be durable, idempotent, retriable, and non-blocking. |
| F3 | **PO status sync** (Draft/To Receive/Completed/Cancelled) | ERP → Portal | ERP webhook if available, else scheduled pull | **Webhook (preferred) with scheduled-pull fallback** | Portal shows a live projection; webhook is timely, pull guarantees convergence when webhooks are absent/lost. |
| F4 | **Reference-data sync** (Currency, Supplier Group, Categories, Incoterm, UoM, Payment Terms) | ERP → Portal | Nightly + on-demand | **Scheduled REST pull (reconcile/upsert)** | Low-volatility, bulk, non-urgent. Portal ships seeded defaults so it never depends on this at runtime. |
| F5 | **ExternalId acknowledgement** (ERP mints naming-series id) | ERP → Portal | Response to F1/F2 publish | **Synchronous response captured by the Outbox dispatcher** | The publish response already carries the new id; capture it inline and persist. |
| F6 | *(optional)* **RFQ / Proposal mirror** | Portal → ERP | RFQ published / Proposal submitted `[ASSUMPTION]` | **Outbox → async publish** | Same durability profile as F1/F2; gated on business confirmation. |
| F7 | **Reconciliation** (drift detection & repair) | Bidirectional | Scheduled (Hangfire recurring) | **Scheduled reconciliation job** | Catches lost webhooks, DLQ residue, and ERP-side edits to ERP-owned fields. |

### Mechanism decision principles

1. **Portal-authored data leaving the portal → always Outbox.** Guarantees the domain transaction and
   the intent-to-publish commit atomically; dispatch is decoupled and retriable. Never a synchronous
   call from a command handler.
2. **ERP-owned data entering the portal → pull-first, webhook-accelerated.** A scheduled pull is the
   correctness backstop; webhooks (when ERPNext exposes them) reduce latency but are treated as *hints*,
   never as the only path — a missed webhook is healed by the next pull/reconcile.
3. **Reference data → scheduled bulk pull**, never per-request, because the portal seeds defaults and
   must run standalone.

---

## 2. The transactional Outbox (backbone of F1/F2/F6)

```
[ Command handler ]
   ├── mutate aggregate (Supplier → Approved)         ┐  ONE EF Core transaction
   └── INSERT OutboxMessage(SupplierUpserted.v1, …)   ┘  → COMMIT
                    │
        Hangfire recurring dispatcher (every ~10s, SKIP LOCKED batch)
                    │
             ACL outbound adapter  ──REST──►  ERPNext
                    │
        on 2xx: mark Sent, persist ExternalId (F5), SyncStatus=Synced
        on transient fail: increment attempts, backoff, leave Pending
        on permanent fail / attempts exhausted: move to Dead-Letter, SyncStatus=Failed, alert
```

- **Ordering:** dispatched **FIFO per aggregate** (partition key = `portalEntityType:portalId`) so a
  create precedes its updates. Cross-aggregate order is not guaranteed (not needed).
- **Delivery:** at-least-once. Consumers (ERPNext side, via the adapter) are made effectively
  exactly-once by [idempotency keys](#4-idempotency).
- **Storage:** `OutboxMessage` table (PostgreSQL), claimed with `FOR UPDATE SKIP LOCKED` to allow
  multiple dispatcher workers without double-send.

### 2.1 `OutboxMessage` (portal-owned) — shape

| Column | Type | Notes |
|---|---|---|
| `id` | GUIDv7 | PK |
| `aggregateType` / `aggregateId` | text / GUIDv7 | partition/ordering key |
| `type` | text | e.g. `SupplierUpserted.v1` |
| `payload` | jsonb | the DTO (see §3) |
| `idempotencyKey` | text (unique) | see §4 |
| `status` | enum | `Pending · Dispatching · Sent · Failed(DeadLettered)` |
| `attemptCount` | int | for backoff/DLQ threshold |
| `nextAttemptAt` | timestamptz | backoff schedule |
| `lastError` | text | last failure detail (also in IntegrationLog) |
| `correlationId` | text | ties to domain AuditLog & traces |
| `createdAt` / `sentAt` | timestamptz | |

---

## 3. Integration contracts

Contracts are **portal-owned, versioned DTOs** — deliberately *not* raw ERPNext doctype JSON. The ACL
translator (Mapperly) maps DTO → ERPNext payload. Field ownership (who wins on conflict) is annotated.

### 3.1 Field-ownership map — Supplier (`SupplierUpserted.v1` → ERPNext `Supplier`)

| Portal field | ERPNext `Supplier` field | Owner | Notes |
|---|---|---|---|
| `legalName` | `supplier_name` (reqd) | Portal authors | ERP `no_copy`; created once. |
| `supplierType` (`Company/Individual/Partnership`) | `supplier_type` (reqd) | Portal | Enum aligns to ERP options exactly. |
| `categoryCode` → group | `supplier_group` | ERP (value set) | Portal sends a *mapped* group; unknown → `Unmapped` sentinel + alert. |
| `countryCode` | `country` (Link→Country) | Portal | ISO → ERP Country name via ACL lookup. |
| `taxId` | `tax_id` | ERP owns post-create | Re-published on portal edit; ERP authoritative. |
| `defaultCurrency` | `default_currency` (Link→Currency) | ERP owns | From reference cache (F4). |
| `paymentTermsCode` | `payment_terms` (Link→Payment Terms Template) | ERP owns | `[ASSUMPTION]` mapping table. |
| `bankAccounts[]` | `accounts[]` / `default_bank_account` | ERP owns | Portal collects superset; maps minimal. |
| `primaryContact{name,email,mobile}` | `supplier_primary_contact` + linked Contact | Portal authors | ERP links a Contact doctype; ACL creates/links. |
| `primaryAddress{…}` | `supplier_primary_address` + linked Address | Portal authors | ACL creates/links Address doctype. |
| `website` | `website` | Portal | — |
| `disabled` (Suspended/Deactivated) | `disabled` / `on_hold` | Portal drives | Portal lifecycle → ERP block flags. |
| — (portal only) | — | Portal | Documents, onboarding history, offerings — **never sent**. |

### 3.2 `SupplierUpserted.v1` — sample payload

```json
{
  "schemaVersion": "1.0",
  "eventType": "SupplierUpserted",
  "eventId": "018f7b2a-1c44-7e21-9b0e-2a1f6c3d4e5f",
  "occurredAt": "2026-08-26T09:14:03Z",
  "correlationId": "sup-onb-3f9c…",
  "idempotencyKey": "supplier:018f6a…:v7",
  "operation": "upsert",
  "externalId": null,
  "supplier": {
    "portalId": "018f6a1e-9d33-7c10-88aa-0b1c2d3e4f50",
    "publicRef": "SUP-2026-000042",
    "legalName": "Palmyra Hospitality Supplies Co.",
    "supplierType": "Company",
    "supplierGroup": "Hospitality - F&B",
    "countryCode": "SY",
    "taxId": "[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]",
    "defaultCurrency": "SYP",
    "paymentTermsCode": "NET30",
    "website": "https://example.sy",
    "primaryContact": { "fullName": "Layla Haddad", "email": "layla@example.sy", "mobile": "+963…" },
    "primaryAddress": { "line1": "…", "city": "Damascus", "countryCode": "SY" },
    "bankAccounts": [ { "bankName": "…", "iban": "…", "currency": "SYP" } ],
    "lifecycleStatus": "Active"
  }
}
```

- `operation: "upsert"` + `externalId: null` → ACL does **create**; a non-null `externalId` → **update**
  the existing ERP `Supplier`.
- `rowVersion` from the aggregate is carried in the envelope for optimistic-concurrency logging.

### 3.3 `AwardCreated.v1` → ERPNext `Purchase Order` — sample payload

```json
{
  "schemaVersion": "1.0",
  "eventType": "AwardCreated",
  "eventId": "018f7b2b-77aa-7f02-9c31-4d5e6f708192",
  "occurredAt": "2026-08-26T15:42:10Z",
  "correlationId": "award-rfq-2026-000123",
  "idempotencyKey": "award:018f9c…:po",
  "award": {
    "portalId": "018f9c22-0e10-7b55-9a12-77aa33bb44cc",
    "publicRef": "AWD-2026-000311",
    "rfqPublicRef": "RFQ-2026-000123",
    "supplierExternalId": "SUP-2026-000042",
    "supplierPortalId": "018f6a1e-9d33-7c10-88aa-0b1c2d3e4f50",
    "company": "[ERP Company — from Organization mapping]",
    "currency": "SYP",
    "transactionDate": "2026-08-26",
    "requiredByDate": "2026-09-15",
    "incoterm": "DAP",
    "namedPlace": "Damascus Central Warehouse",
    "items": [
      { "lineRef": "1", "description": "Bath linen set — 400 GSM",
        "uom": "Set", "qty": 500, "rate": 12000, "amount": 6000000,
        "erpItemCode": "[ASSUMPTION — item master mapping]" }
    ],
    "grandTotal": 6000000
  }
}
```

Notes grounded in the ERPNext doctypes:

- ERP `Purchase Order` requires `company`, `supplier`, `currency`, `items[]`; naming series
  `PUR-ORD-.YYYY.-`. The ACL sets `supplier` = `supplierExternalId` (must be non-null → award publish
  **depends on** the supplier having been synced first; see §4.2 ordering).
- `incoterm`/`named_place` map straight through (fields exist on PO/RFQ/SQ).
- `erpItemCode` mapping is `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` — the portal's offering/RFQ
  item may not correspond 1:1 to an ERP `Item` master; a mapping table or "non-stock/description-only"
  line strategy is needed.

---

## 4. Idempotency

At-least-once delivery means retries can re-send. Every outbound message carries an **`idempotencyKey`
derived from portal identity** (never ERP identity, which may not exist yet):

- Supplier create/update: `supplier:{portalId}:v{rowVersion}` — a re-send of the *same* version is a
  no-op; a new version is a distinct upsert.
- Award → PO: `award:{portalId}:po` — exactly one PO per award, ever.

Enforcement is **belt-and-braces** because ERPNext has no native idempotency header:

1. **Portal side:** the `ExternalIdRegistry` is checked first — if a mapping already exists for the key,
   the message is short-circuited to `Sent` (the PO/Supplier already exists).
2. **ERP side (via ACL):** before create, the adapter does a **guarded existence check** (query by a
   stored external-reference field, e.g. PO custom field `portal_award_ref`, or Supplier by a stored
   `portal_supplier_ref`) `[ASSUMPTION — requires a custom field or naming convention in ERPNext]`. If
   found, it **updates/links** instead of creating a duplicate.
3. The `idempotencyKey` column is **UNIQUE** in the Outbox to prevent double-enqueue at the source.

### 4.2 Cross-flow ordering dependency

`AwardCreated` → PO requires a valid ERP `supplier` id. The dispatcher enforces: **if the award's
supplier has `SyncStatus != Synced` (no `ExternalId` yet), the award message is deferred** (re-queued
with backoff) until the supplier sync completes, rather than failing. This is a data dependency, not an
error.

---

## 5. Retry, backoff & timeouts

| Aspect | Policy |
|---|---|
| **Classification** | 2xx → success. 408/429/5xx/timeout/connection → **transient** (retry). 400/401/403/409/422 → **permanent** (no blind retry; 401/403 pauses the flow + alerts credentials). |
| **Backoff** | Exponential with jitter: `min(2^attempt · base, cap)`, base 30s, cap 1h; full-jitter to avoid thundering herd on ERP recovery. |
| **Max attempts** | Default **8** before dead-letter (~ spans hours). Configurable per message type. |
| **Timeouts** | Per-call HTTP timeout 30s; total dispatch worker budget bounded so one stuck call can't starve the batch. |
| **Circuit breaker** | Per-endpoint breaker (e.g. Polly): after N consecutive failures, open for a cool-off, fast-fail dispatches into `Pending` (no wasted calls), auto half-open probe. Keeps ERP outages cheap. |
| **Poison protection** | A message that throws in *translation* (not transport) is dead-lettered immediately — retrying won't fix a mapping bug. |

---

## 6. Dead-letter handling

- Messages exhausting retries (or hitting a permanent error) move to **`status = Failed`** and are
  surfaced in a **Dead-Letter queue view** in the admin/integration console.
- Each DLQ entry keeps: full payload, all attempts, last error, correlationId, target endpoint.
- **Operator actions:** inspect → fix data/mapping → **replay** (re-enqueue with a fresh attempt
  counter) or **discard with reason** (audited). Replay is idempotent thanks to §4.
- The affected aggregate shows `SyncStatus = Failed` in the UI with a human-readable reason and a
  "Retry sync" affordance for privileged roles (`system_admin`); the **core workflow is never blocked**
  by a DLQ entry.

---

## 7. Reconciliation jobs

Async + at-least-once + optional webhooks ⇒ drift is inevitable; reconciliation is the convergence
guarantee. Implemented as **Hangfire recurring jobs**.

| Job | Cadence | What it does |
|---|---|---|
| **Supplier reconcile** | Nightly | For suppliers `Synced`, compare ERP-owned fields (tax_id, group, currency, payment terms, disabled) with ERP; on divergence → **flag for review** (ERP authoritative for ERP-owned fields), do not silent-overwrite. Re-publish suppliers stuck `Pending` past SLA. |
| **PO status reconcile** | Every 15 min | Pull PO status for awards with an `ExternalPurchaseOrderRef`; heal any webhook (F3) that was lost; update the projection. |
| **Reference-data sync** (F4) | Nightly + on-demand | Bulk pull Currency/Supplier Group/Categories/Incoterm/UoM/Payment Terms; upsert reference cache; mark removed items inactive (never hard-delete referenced ones). |
| **Outbox sweeper** | Every 5 min | Re-arm messages whose worker died mid-`Dispatching`; report stuck/backlogged counts. |
| **Orphan/duplicate audit** | Weekly | Detect portal aggregates `Synced` with no live ERP counterpart (and vice-versa) → operator report. |

---

## 8. Sync-status model, logging & monitoring

### 8.1 `SyncStatus` (per ERP-syncable aggregate)

`NotApplicable · NotSynced · Pending · Dispatching · Synced · Failed · Divergent`

- Stored alongside `ExternalId`, `LastSyncedAt`, `RowVersion` (foundational §4).
- Surfaced in the UI as a calm, non-alarming badge (e.g. a small "ERP: synced/pending" chip), themed to
  the [design tokens](../architecture/00-foundational-decisions.md#7-design-system-tokens-canonical);
  RTL-aware. `Failed`/`Divergent` visible only to authorized roles.

### 8.2 IntegrationLog & telemetry

- **`IntegrationLog`** (structured, queryable): one row per seam crossing —
  `direction, flow(F1..F7), messageType, idempotencyKey, endpoint, httpStatus, latencyMs, attempt,
  outcome, correlationId, errorSummary`. Linked to the domain `AuditLog` by `correlationId`.
- **Serilog (JSON)** for logs; **OpenTelemetry** traces span the whole hop
  (command → outbox → dispatch → ERP call), with `correlationId` as the trace/baggage key.
- **Metrics/alerts:** outbox backlog depth & age, dispatch success rate, p95 ERP latency, DLQ size,
  reconcile divergences found, circuit-breaker state, webhook-vs-pull heal count. Alert thresholds:
  DLQ > 0 (warn), outbox oldest-pending age > SLA, breaker open > N min, sustained 401/403.

---

## 9. Failure & degraded-mode behavior

Degraded mode is **designed and tested**, per the availability principle
([boundary §6](./ERP-INTEGRATION-BOUNDARY.md#6-the-portal-never-blocks-on-erp-availability-principle)).

| Failure | User-visible effect | System behavior |
|---|---|---|
| ERP fully **down** | None on core flows. Aggregates show `Pending`. | Outbox accumulates; breaker opens; reconcile/backoff resumes on recovery. |
| ERP **slow** | None (async). | Timeouts + backoff; breaker guards worker pool. |
| **Auth failure** (401/403) | Admin banner in integration console. | Flow paused, alert raised; no destructive retry; resumes after credential fix. |
| **Reference sync fails** | None — **portal-seeded defaults** stay in effect. | Cache simply not refreshed; retried nightly/on-demand. |
| **Webhook lost** (F3) | Slightly stale PO status. | Scheduled pull reconciles within its cadence. |
| **Mapping/validation error** (422) | Aggregate shows `Failed` + reason to admins. | Immediate dead-letter; operator fixes data/mapping, replays. |
| **Divergence** on ERP-owned field | `Divergent` badge to admins; ERP value shown as authoritative. | Reconcile flags, never silent-overwrites. |
| **Portal** down | Standard portal HA concern; Outbox is durable so nothing is lost on restart. | Dispatcher resumes from committed Outbox. |

---

## 10. Sequence diagram — Supplier master sync (F1 + F5)

```mermaid
sequenceDiagram
    autonumber
    actor R as Onboarding Reviewer
    participant API as Portal API (Command handler)
    participant DB as PostgreSQL (Supplier + Outbox)
    participant DIS as Outbox Dispatcher (Hangfire)
    participant ACL as ACL Adapter + Translator
    participant ERP as ERPNext /api/resource/Supplier

    R->>API: Approve supplier (supplier.approve)
    API->>DB: TX { Supplier→Approved; INSERT Outbox(SupplierUpserted.v1) } COMMIT
    API-->>R: 200 OK (no ERP wait) · SyncStatus=Pending
    Note over DIS: runs every ~10s, claims batch FOR UPDATE SKIP LOCKED
    DIS->>DB: fetch Pending (idempotencyKey unique)
    DIS->>ACL: dispatch(payload)
    ACL->>ACL: check ExternalIdRegistry (idempotency)
    alt already mapped
        ACL-->>DIS: no-op (already Synced)
    else not mapped
        ACL->>ERP: POST Supplier (translated JSON)
        alt 2xx
            ERP-->>ACL: 200 { name: "SUP-2026-000042" }
            ACL->>DB: set ExternalId, SyncStatus=Synced, LastSyncedAt (F5)
            ACL->>DB: registry.upsert(portalId ↔ SUP-2026-000042)
            ACL->>DB: Outbox→Sent; IntegrationLog(success)
        else transient (5xx/timeout)
            ERP-->>ACL: error
            ACL->>DB: attempts++, nextAttemptAt=backoff; IntegrationLog(retry)
        else permanent (422)
            ERP-->>ACL: 422 validation
            ACL->>DB: Outbox→Failed(DLQ); SyncStatus=Failed; alert
        end
    end
```

---

## 11. Sequence diagram — Award → Purchase Order (F2, with dependency + status sync F3)

```mermaid
sequenceDiagram
    autonumber
    actor M as Procurement Manager
    participant API as Portal API
    participant DB as PostgreSQL (Award + Outbox)
    participant DIS as Outbox Dispatcher
    participant ACL as ACL Adapter + Translator
    participant ERP as ERPNext /api/resource/Purchase Order

    M->>API: Finalize award (award.approve)
    API->>DB: TX { Award→Awarded; INSERT Outbox(AwardCreated.v1) } COMMIT
    API-->>M: 200 OK · Award SyncStatus=Pending
    DIS->>DB: claim AwardCreated
    DIS->>ACL: dispatch(award payload)
    ACL->>DB: lookup supplier ExternalId
    alt supplier not yet Synced
        ACL->>DB: defer award (re-queue w/ backoff)
        Note over ACL,DB: ordering dependency (§4.2) — no error
    else supplier Synced (has ExternalId)
        ACL->>ERP: POST Purchase Order { supplier, company, currency, items[] }
        alt 2xx
            ERP-->>ACL: 200 { name: "PUR-ORD-2026-000311" }
            ACL->>DB: set ExternalPurchaseOrderRef, SyncStatus=Synced
            ACL->>DB: registry.upsert(awardId ↔ PUR-ORD-2026-000311)
        else transient / permanent
            ACL->>DB: backoff (retry) OR DLQ + SyncStatus=Failed + alert
        end
    end

    Note over ERP,DB: Later — PO lifecycle changes in ERP
    ERP-->>ACL: webhook: PO status = To Receive  (F3, if available)
    ACL->>DB: update PO status projection on Award
    Note over DIS,DB: Fallback — every 15 min reconcile pulls PO status (heals lost webhooks)
```

---

## 12. Testing the integration

Per [foundational §2 testing](../architecture/00-foundational-decisions.md#backend):

- **Contract tests** on every DTO ↔ ERPNext-doctype translation (Mapperly), pinned to the real field
  names/enums extracted from the doctype JSONs.
- **Outbox integration tests** (Testcontainers Postgres): atomic commit, FIFO-per-aggregate, unique
  idempotency key, SKIP LOCKED concurrency, replay is idempotent.
- **Resilience tests:** simulated ERP 5xx/timeout/401/422 → correct retry/backoff/DLQ/breaker behavior;
  degraded-mode assertions (approval succeeds with ERP down).
- **Reconciliation tests:** injected drift & lost-webhook scenarios converge.
- A **fake ERPNext adapter** (WireMock-style) reproduces naming-series responses so tests never need a
  live ERP.

---

## 13. Open assumptions (mirror to `docs/product/ASSUMPTIONS.md`)

- `[ASSUMPTION]` ERPNext auth mechanism (API key/secret vs OAuth2) and base URL per environment.
- `[ASSUMPTION]` Whether RFQ/Proposal are mirrored to ERP at all (F6) or stay portal-only until PO.
- `[ASSUMPTION]` Existence of custom reference fields in ERPNext (`portal_supplier_ref`,
  `portal_award_ref`) for server-side idempotency; else rely on registry + naming convention.
- `[ASSUMPTION]` Portal RFQ item ↔ ERP `Item` master mapping (or description-only PO lines).
- `[ASSUMPTION]` Payment-terms / supplier-group / category code mapping tables between portal & ERP.
- `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` Any Syrian tax/withholding fields on the supplier
  master — kept generic, never invented.
