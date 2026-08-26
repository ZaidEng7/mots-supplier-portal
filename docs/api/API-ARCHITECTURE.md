# API Architecture — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Principal Architect · **Date:** 2026-08-26
> Canonical inputs: [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md).
> This document defines the HTTP contract every backend vertical slice and every frontend TanStack
> Query hook must obey. Where a rule depends on unconfirmed Syrian business/legal detail it is tagged
> **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** and mirrored in
> [`../product/ASSUMPTIONS.md`](../product/ASSUMPTIONS.md).

---

## 1. Scope & principles

The portal exposes a **single first-party JSON/HTTPS API** consumed by the React 19 SPA and, later, by
the ERPNext integration layer (server-to-server). It is built on **ASP.NET Core Minimal APIs (.NET 10)**
with **feature-grouped endpoints** — one endpoint file per vertical slice, thin handlers dispatching to
`Application` layer command/query handlers (no MediatR; a DI-resolved dispatcher).

Design principles, in priority order:

1. **Resource-oriented REST** with predictable, boring conventions — no RPC verbs in paths.
2. **Contract stability** — the SPA and the ERP ACL depend on stable shapes; breaking changes require a
   new API version.
3. **Opaque public identifiers** — internal GUIDv7 / integer PKs are **never** exposed in URLs, payloads,
   or errors. Public references are human-readable short codes (`RFQ-2026-000123`) or slugs.
4. **Domain is the authority** — illegal state-machine transitions and RBAC violations are rejected by
   the API with typed errors, never merely hidden in the UI.
5. **Safe by construction** — idempotency for POST, optimistic concurrency for mutation, correlation for
   every request, RFC 9457 for every error.
6. **Localized** — every human-facing message (including validation) is returned in **Arabic and English**.

Non-goals: no HATEO-heavy hypermedia, no GraphQL, no public/partner API surface in v1 (a future,
separately versioned surface may be added under `/partner/`).

---

## 2. Base URL, versioning & environments

| Concern | Rule |
|---|---|
| Base path | `https://{host}/api/v1` |
| Versioning strategy | **URI-path major version** (`/api/v1`, `/api/v2`). Chosen over header/media-type versioning for cache-key clarity and trivial routing. |
| Version bump triggers | Removing/renaming a field, changing a field type, tightening validation, changing an enum's meaning, or altering an error `type`. **Additive** changes (new optional field, new endpoint, new enum value documented as open) are **non-breaking** and ship within the current version. |
| Deprecation | A retiring version/endpoint returns `Deprecation: true` and `Sunset: <HTTP-date>` headers (RFC 8594) plus a `Link` to migration docs. Minimum **90-day** overlap. |
| Health/ops (unversioned) | `GET /health/live`, `GET /health/ready` (no auth), `GET /api/v1/meta` (build/version/commit). |
| Docs | `GET /openapi/v1.json` (native .NET OpenAPI) and **Scalar UI** at `/scalar` (non-prod; behind admin auth in prod). |

Enum values are **string** (never ordinal integers) so the wire contract is stable across DB changes —
e.g. `"UnderReview"`, not `3`.

---

## 3. Resource naming & URL structure

- **Nouns, plural, kebab-case** collections: `/suppliers`, `/rfqs`, `/proposals`, `/evaluation-templates`.
- **Public identifier in the path** — the opaque short code / slug, never the GUID:
  - `GET /api/v1/rfqs/RFQ-2026-000123`
  - `GET /api/v1/suppliers/SUP-2026-004512`
- **Sub-resources** express aggregate composition (bounded by the [domain model](../architecture/DOMAIN-MODEL.md)):
  - `/suppliers/{supplierCode}/documents`
  - `/rfqs/{rfqCode}/invitations`
  - `/rfqs/{rfqCode}/clarifications`
  - `/rfqs/{rfqCode}/proposals` · `/proposals/{proposalCode}/items`
  - `/evaluations/{evaluationCode}/assignments` · `/evaluations/{evaluationCode}/scores`
- **State transitions are sub-resource POSTs**, not PATCH-of-status (keeps transitions permission-guarded,
  auditable, and idempotent). The verb after the colon is a **transition command**, aligned to the
  canonical state machines in [`BUSINESS-PROCESSES.md`](../product/BUSINESS-PROCESSES.md):
  - `POST /rfqs/{rfqCode}/publish`
  - `POST /rfqs/{rfqCode}/cancel`
  - `POST /proposals/{proposalCode}/submit`
  - `POST /proposals/{proposalCode}/withdraw`
  - `POST /suppliers/{supplierCode}/onboarding/submit`
  - `POST /suppliers/{supplierCode}/documents/{documentId}/approve`
  - `POST /awards/{awardCode}/approve`

**Rule of thumb:** if an operation moves an aggregate through its state machine, it is a named transition
endpoint (`POST …/{verb}`) — not a field write. Illegal transitions return `409 Conflict`
(`type: …/errors/invalid-state-transition`) listing the current state and the allowed next states.

### 3.1 Path grammar

```
/api/v1/{collection}/{publicId}/{sub-collection}/{subId}/{transition?}
```

- `{publicId}` matches short-code patterns (`^[A-Z]{2,4}-\d{4}-\d{6}$`) or slugs; unmatched shapes →
  `404` (never leak whether a GUID exists).
- Query strings carry paging/filter/sort/field-selection only — **never** identifiers of the acting user
  or secrets (see [security constraints](../security/)).

---

## 4. HTTP semantics & status codes

| Method | Use | Idempotent | Body |
|---|---|---|---|
| `GET` | Read resource/collection | Yes | none |
| `POST` | Create; **or** invoke a named state transition | No (use `Idempotency-Key`) | JSON |
| `PUT` | Full replace of a mutable resource (rare; used for draft docs) | Yes | JSON |
| `PATCH` | Partial update (JSON Merge Patch, RFC 7396) of draft-editable resources | Yes* | JSON |
| `DELETE` | Remove/soft-delete per lifecycle | Yes | none |

\* `PATCH` is idempotent at the field level; it still requires `If-Match` (see §8).

### 4.1 Status code contract

| Code | When |
|---|---|
| `200 OK` | Successful read or transition returning a body |
| `201 Created` | Resource created; `Location` header → canonical URL; body = created resource |
| `202 Accepted` | Work queued (e.g. document virus-scan, ERP sync, bulk invite); body includes a status URL |
| `204 No Content` | Successful mutation with no body (e.g. `DELETE`, some transitions) |
| `303 See Other` | Post-redirect for flows that must not be re-POSTed (rare; SPA uses JSON) |
| `304 Not Modified` | Conditional `GET` with matching `ETag` / `If-None-Match` |
| `400 Bad Request` | Malformed JSON, wrong types, missing required — RFC 9457 body |
| `401 Unauthorized` | Missing/invalid/expired bearer token; `WWW-Authenticate: Bearer` |
| `403 Forbidden` | Authenticated but lacks the required `resource.action` permission or row-scope |
| `404 Not Found` | Unknown public id, or hidden by row-scope (indistinguishable by design) |
| `405 Method Not Allowed` | Method not supported on the resource |
| `409 Conflict` | Illegal state transition, unique-constraint violation, or duplicate submission |
| `410 Gone` | Sunset endpoint/version after its `Sunset` date |
| `412 Precondition Failed` | `If-Match` did not match current `ETag` (stale write) |
| `413 Payload Too Large` | Upload exceeds size limit |
| `415 Unsupported Media Type` | Non-JSON body where JSON required, or disallowed upload MIME |
| `422 Unprocessable Content` | Well-formed but **business-rule/validation** failure — the primary validation code |
| `423 Locked` | Resource temporarily locked (e.g. RFQ under consolidation) `[ASSUMPTION]` |
| `428 Precondition Required` | Mutating endpoint called without required `If-Match` |
| `429 Too Many Requests` | Rate limit exceeded; `Retry-After` header |
| `500 Internal Server Error` | Unhandled; RFC 9457 body with `traceId`, **no** stack/detail leak |
| `503 Service Unavailable` | Dependency down (DB) or maintenance; `Retry-After` |

**400 vs 422:** `400` = the request could not be parsed/bound (bad JSON, wrong primitive type). `422` =
the request parsed fine but failed FluentValidation or a domain invariant (this is where field-level,
bilingual validation errors live).

---

## 5. Standard response envelopes

### 5.1 Single resource — **no wrapper**

A single resource is returned as the bare object (plus headers `ETag`, `Correlation-Id`). Wrapping single
resources adds noise for TanStack Query with no benefit.

### 5.2 Collections — **list envelope**

Every list endpoint returns a consistent envelope so table components and query hooks are uniform:

```jsonc
{
  "data": [ /* array of resource objects */ ],
  "pagination": {
    "mode": "cursor",              // "cursor" | "page"
    "nextCursor": "eyJpZCI6IjAx…", // null when no more (cursor mode)
    "prevCursor": null,
    "pageSize": 20,
    "totalCount": 143,            // present only when cheap/requested (see §6.1)
    "hasMore": true
  },
  "meta": {
    "sort": "-submittedAt",
    "filtersApplied": ["state=Submitted", "category=hospitality-linens"]
  }
}
```

Empty results return `data: []` with `200`, never `404`.

---

## 6. Pagination, filtering, sorting, sparse fields

### 6.1 Pagination — dual mode

Both modes are supported; each endpoint documents its **default**. **Cursor is the default** for large,
frequently-mutated, or infinite-scroll collections (RFQs, proposals, audit log, notifications). **Page**
is available for admin/back-office tables that show a pager with total counts.

| Mode | Query params | Semantics |
|---|---|---|
| Cursor (default) | `?cursor=<opaque>&pageSize=20` | Keyset pagination over a stable sort key (GUIDv7 is time-ordered, giving natural, gap-free paging). Opaque base64url cursor encodes the last sort tuple + direction. Immune to insert-shift. `totalCount` omitted unless `?withCount=true`. |
| Page | `?page=2&pageSize=20` | Offset paging for finite admin grids. Always returns `totalCount`. Hard cap `page*pageSize ≤ 10 000` to protect the DB; beyond that → `422` advising cursor mode. |

Constraints: `pageSize` default **20**, min **1**, max **100** (`> 100` → clamped + `Warning` header).
Cursors are **not** shareable across users/filters (they encode the exact query context and are validated).

### 6.2 Filtering

Explicit, whitelisted, type-checked query params per endpoint — **no** generic query-language passthrough
(avoids injection and unbounded scans).

- Equality: `?state=Submitted&category=hospitality-linens`
- Multi-value (OR): `?state=Submitted,UnderReview`
- Ranges: `?submittedAtFrom=2026-08-01&submittedAtTo=2026-08-26`, `?priceMin=…&priceMax=…`
- Boolean: `?isExpiringSoon=true`
- Free-text search: `?q=linen` (server decides searched fields; Arabic-aware, diacritic-insensitive
  collation) `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on which supplier fields are searchable.
- Unknown filter key → `422` (`type: …/errors/unknown-filter`) rather than silent ignore.

### 6.3 Sorting

`?sort=field` ascending, `?sort=-field` descending; multi-key comma-separated: `?sort=-submittedAt,name`.
Only whitelisted sort keys per endpoint; unknown key → `422`. Default sort documented per endpoint (e.g.
RFQ list default `-publishedAt`).

### 6.4 Sparse fieldsets

`?fields=code,name,state,submittedAt` returns only requested top-level fields (plus always-present `code`).
Reduces payload for table views. Invalid field name → ignored with a `Warning` header (not fatal).
Nested expansion is **opt-in and bounded**: `?expand=items,documents` (each endpoint whitelists expandable
sub-resources; depth 1 only, to protect against N+1 and payload blow-up).

---

## 7. Error model — RFC 9457 `application/problem+json`

Every non-2xx (except `304`) returns **`application/problem+json`** (RFC 9457, the successor to RFC 7807).
Base shape:

```jsonc
{
  "type": "https://api.mots-portal.sy/errors/validation",   // stable, dereferenceable slug
  "title": "One or more validation errors occurred.",       // English, short, human
  "status": 422,
  "detail": "The proposal cannot be submitted with an empty line-item list.",
  "instance": "/api/v1/rfqs/RFQ-2026-000123/proposals/PRO-2026-000891/submit",
  "code": "PROPOSAL_ITEMS_REQUIRED",   // machine-stable app error code (SCREAMING_SNAKE)
  "traceId": "0af7651916cd43dd8448eb211c80319c",  // W3C trace-id; ties to logs/OTel span
  "correlationId": "b3f2c1a0-7e6d-4c2b-9a11-2d3e4f5a6b7c"
}
```

- `type` is a **stable URI slug per error category** (documented list below); clients switch on `type`/`code`,
  **never** on `title`/`detail` (those may be localized/reworded).
- `code` is an application-level, machine-stable identifier for programmatic handling and analytics.
- `traceId` is the **W3C Trace Context** trace-id (also in the `traceparent`/response headers), enabling
  one-click log/OTel correlation. Always present, including on `500`.
- `500` responses **never** include stack traces, SQL, or internal messages — only `type`, `title`
  (generic), `status`, `traceId`, `correlationId`.

### 7.1 Canonical error `type` catalog (extract)

| `type` slug | HTTP | Typical `code`s |
|---|---|---|
| `/errors/validation` | 422 | `VALIDATION_FAILED`, field-specific codes |
| `/errors/malformed-request` | 400 | `MALFORMED_JSON`, `TYPE_MISMATCH` |
| `/errors/unauthorized` | 401 | `TOKEN_MISSING`, `TOKEN_EXPIRED`, `TOKEN_INVALID` |
| `/errors/forbidden` | 403 | `PERMISSION_DENIED`, `OUT_OF_SCOPE` |
| `/errors/not-found` | 404 | `RESOURCE_NOT_FOUND` |
| `/errors/invalid-state-transition` | 409 | `ILLEGAL_TRANSITION` |
| `/errors/conflict` | 409 | `DUPLICATE_RESOURCE`, `UNIQUE_VIOLATION` |
| `/errors/precondition-failed` | 412 | `ETAG_MISMATCH` |
| `/errors/precondition-required` | 428 | `IF_MATCH_REQUIRED` |
| `/errors/idempotency-conflict` | 409 | `IDEMPOTENCY_KEY_REUSED` |
| `/errors/rate-limited` | 429 | `RATE_LIMIT_EXCEEDED` |
| `/errors/payload-too-large` | 413 | `FILE_TOO_LARGE` |
| `/errors/unsupported-media-type` | 415 | `MIME_NOT_ALLOWED` |
| `/errors/dependency-unavailable` | 503 | `DB_UNAVAILABLE`, `STORAGE_UNAVAILABLE` |
| `/errors/internal` | 500 | `INTERNAL_ERROR` |

### 7.2 Validation error shape (bilingual, field-scoped)

Validation failures (`422`) extend the base problem with an `errors` array. Each field carries **both**
Arabic and English messages so the SPA renders in the active locale without a round-trip, and back-office
audit logs are readable in both:

```jsonc
{
  "type": "https://api.mots-portal.sy/errors/validation",
  "title": "One or more validation errors occurred.",
  "status": 422,
  "code": "VALIDATION_FAILED",
  "traceId": "0af7651916cd43dd8448eb211c80319c",
  "correlationId": "b3f2c1a0-7e6d-4c2b-9a11-2d3e4f5a6b7c",
  "errors": [
    {
      "field": "email",
      "code": "EMAIL_INVALID",
      "messages": {
        "ar": "صيغة البريد الإلكتروني غير صحيحة.",
        "en": "The email address format is invalid."
      }
    },
    {
      "field": "items[0].unitPrice",
      "code": "PRICE_NON_POSITIVE",
      "messages": {
        "ar": "يجب أن يكون سعر الوحدة أكبر من صفر.",
        "en": "Unit price must be greater than zero."
      },
      "attemptedValue": 0
    }
  ]
}
```

- `field` uses **dot/bracket paths** matching the request JSON (`items[0].unitPrice`) so React Hook Form
  can map errors straight onto inputs.
- `code` is field-error stable; messages are localized via i18next resource keys server-side (the source
  of truth for validation text is `Application` layer FluentValidation + a shared message catalog aligned
  with the frontend Zod schemas — see [`00-foundational-decisions.md §Frontend`](../architecture/00-foundational-decisions.md)).
- `attemptedValue` is included only for non-sensitive fields (never for passwords/tokens).

---

## 8. Concurrency, idempotency & correlation

### 8.1 Optimistic concurrency — `ETag` / `RowVersion`

Every mutable aggregate carries a `RowVersion` (PostgreSQL `xmin`-backed or explicit `bytea`/`bigint`
version column per [domain rules](../architecture/DOMAIN-MODEL.md)). It surfaces on the wire as a strong
`ETag`.

- Reads return `ETag: "W/…"` (the current `RowVersion`, base64url).
- Mutating `PUT`/`PATCH`/transition `POST` on an existing resource **must** send `If-Match: "<etag>"`.
  - Missing `If-Match` → `428 Precondition Required` (`IF_MATCH_REQUIRED`).
  - Stale `If-Match` → `412 Precondition Failed` (`ETAG_MISMATCH`) — the SPA refetches and reconciles.
- Conditional reads: `If-None-Match` → `304 Not Modified` (saves bandwidth on polling, e.g. RFQ detail).

This prevents lost updates when two procurement officers edit the same RFQ draft, or a supplier edits a
proposal in two tabs.

### 8.2 Idempotency for POST — `Idempotency-Key`

All **non-idempotent POST** endpoints (resource creation and state transitions) accept an
`Idempotency-Key: <client-generated-uuid>` request header. Contract:

1. The server persists `{key, requestFingerprint(hash of method+path+body), userId, responseSnapshot}` for
   **24 hours** `[ASSUMPTION]` in a dedicated store (Postgres table, GC'd by Hangfire).
2. First call with a key → processed normally; response stored.
3. **Retry with the same key + same fingerprint** → the stored response is replayed verbatim (same status,
   same body, header `Idempotency-Replayed: true`). This makes network-retry-safe submissions (a supplier
   double-clicking **Submit Proposal** cannot create two proposals).
4. Same key + **different** fingerprint → `409` (`IDEMPOTENCY_KEY_REUSED`).
5. The SPA generates one key per user submission intent (e.g. per "Submit" click) via `crypto.randomUUID()`.

`Idempotency-Key` is **required** for financially/legally significant transitions: `proposal.submit`,
`award.approve`, `rfq.publish` — a missing key on these returns `428` (`IDEMPOTENCY_KEY_REQUIRED`).

### 8.3 Correlation & tracing

| Header | Direction | Contract |
|---|---|---|
| `Correlation-Id` | in/out | Client **may** send a UUID; if absent the API generates one. Always echoed on the response and stamped on every log line, `AuditLog` row, `OutboxMessage`, and downstream ERP call. This is the **business** correlation id (survives across async Outbox → ERP hops). |
| `traceparent` / `tracestate` | in/out | **W3C Trace Context** for OpenTelemetry. The `trace-id` also appears as `traceId` in every problem+json and in Serilog JSON. |
| `Request-Id` | out | Per-request unique id (distinct from correlation, which can span requests). |

A single supplier onboarding submission is traceable end-to-end: SPA `Correlation-Id` → API handler →
domain events → Outbox → (future) ERP supplier-create, all sharing the same `Correlation-Id`.

---

## 9. AuthN & AuthZ

### 9.1 Authentication — Bearer JWT + rotating refresh

- **Access token:** short-lived JWT (**15 min** `[ASSUMPTION]`), `Authorization: Bearer <jwt>`. Claims:
  `sub` (opaque user id), `email`, `roles`, flattened `perm` (permission) claims, `supplierId?` /
  `orgId?` (row-scope anchors), `locale`, `amr` (auth methods, incl. `mfa`), `jti`, `exp`.
- **Refresh token:** opaque, long-lived, **rotating & one-time-use**, stored server-side (hashed). Sent by
  the SPA to `POST /auth/refresh`; each refresh **rotates** (old token invalidated; reuse of a rotated
  token ⇒ token-theft detection → whole family revoked). Delivered as an **HttpOnly, Secure, SameSite=Strict
  cookie** `[ASSUMPTION]` (mitigates XSS token theft; access token kept in memory only).
- **MFA-ready:** ASP.NET Core Identity 2FA; when a policy requires MFA, tokens without `amr: mfa` are
  rejected on protected endpoints with `403` (`MFA_REQUIRED`).
- **IdP-swappable:** the JWT issuance is behind an abstraction so a future Keycloak/Entra IdP replaces
  local Identity without changing endpoint code.

Credential input (passwords, tokens, MFA secrets) is handled exclusively by the client against the auth
endpoints — the API never accepts credentials in query strings or logs them.

### 9.2 Authorization — permission-scoped RBAC + row-scoping

- Every protected endpoint declares a **required permission** `resource.action` (e.g. `rfq.publish`,
  `proposal.submit`, `evaluation.score`, `award.approve`, `audit.read`) enforced by an ASP.NET Core
  **policy handler** — mapped from the `perm` claims. Missing permission → `403` (`PERMISSION_DENIED`).
- **Row-scoping** is enforced server-side in the `Application`/`Infrastructure` query layer, not just the
  policy:
  - `supplier_admin` / `supplier_user` → only their own `supplierId`.
  - `procurement_officer` / `procurement_manager` / `evaluator` → their `orgId` scope.
  - `ministry_viewer` → **read-only**, cross-org **aggregate** access; commercial values gated
    **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** (see [`OPEN-QUESTIONS.md`](../product/OPEN-QUESTIONS.md)).
  - `system_admin` → global.
- Out-of-scope access to an existing resource returns **`404`** (not `403`) to avoid leaking existence,
  **except** where the persona legitimately shares the collection (then `403` with `OUT_OF_SCOPE`).
- The UI re-checks permissions **only** to hide affordances; the API is the sole authority.

All auth/authz outcomes on state changes are written to [`AuditLog`](../architecture/DOMAIN-MODEL.md)
with actor, permission, from→to state, reason, and `correlationId`.

---

## 10. Media, uploads & rate limiting

- **Content types:** requests/responses `application/json; charset=utf-8`; errors `application/problem+json`.
  File uploads use `multipart/form-data`.
- **Uploads** (`SupplierDocument`, RFQ/proposal attachments): allowed MIME whitelist (PDF, JPEG, PNG, and
  common office docs `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`), max size **20 MB/file** `[ASSUMPTION]`.
  Flow: `POST …/documents` (metadata + file) → `202 Accepted` with a document id in state `Uploaded`; an
  async Hangfire job runs virus scan + validation, transitioning to `UnderReview` (see the document state
  machine in [`00-foundational-decisions.md §5`](../architecture/00-foundational-decisions.md)). Files are
  stored via the `IFileStorage` abstraction (local dev / S3-compatible prod); download is via short-lived,
  scoped, pre-signed URLs — never a raw public path.
- **Rate limiting** (ASP.NET Core built-in rate limiter, per-user + per-IP):
  - Auth endpoints (`/auth/*`): strict fixed-window (e.g. **10 attempts / 5 min / IP+account**) with
    exponential lockout on `login` to blunt credential stuffing.
  - General authenticated API: token-bucket (e.g. **100 req/min/user** sustained, burst 200) `[ASSUMPTION]`.
  - Exceeded → `429` with `Retry-After` and `RateLimit-*` headers (draft IETF `RateLimit` policy).
- **Response compression** (Brotli/Gzip) and `Cache-Control` (private, `no-store` for authenticated
  resources; `ETag`-based revalidation for cacheable reference data like `Category` tree).

---

## 11. OpenAPI, discoverability & client generation

- **Native .NET OpenAPI** generates `/openapi/v1.json` from the Minimal API endpoint metadata,
  FluentValidation-derived constraints, and typed results.
- **Scalar** renders interactive docs at `/scalar` (open in non-prod; auth-gated in prod). Swashbuckle is
  intentionally **not** used (see canonical tech decisions).
- The OpenAPI doc is the contract source for: SPA type generation (`openapi-typescript` → typed TanStack
  Query hooks) and the ERP ACL client. CI fails on an undocumented breaking diff (spectral/oasdiff gate).
- Every endpoint documents: permission required, request/response schema, all error `type`s it can emit,
  pagination mode, and whether `Idempotency-Key` / `If-Match` are required.

---

## 12. Concrete endpoint reference — first vertical slices

All examples are `/api/v1`-prefixed. Headers common to authenticated calls: `Authorization: Bearer …`,
`Correlation-Id`, `Accept-Language: ar` (drives localized `title`/`detail`/validation messages).

### 12.1 Auth

#### `POST /auth/register` — supplier self-registration (starts onboarding at `Draft`)

`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` self-registration is open (vs invite-only) — see
[`OPEN-QUESTIONS.md`](../product/OPEN-QUESTIONS.md).

Request (`Idempotency-Key` recommended):
```json
{
  "email": "purchasing@nour-linens.sy",
  "password": "••••••••••••",
  "supplierLegalName": "شركة نور للمنسوجات",
  "supplierDisplayName": "Nour Linens",
  "representativeName": "ليان الأحمد",
  "phone": "+963 11 555 0100",
  "preferredLocale": "ar"
}
```
Response `201 Created` — `Location: /api/v1/suppliers/SUP-2026-004512`:
```json
{
  "supplierCode": "SUP-2026-004512",
  "onboardingState": "Draft",
  "email": "purchasing@nour-linens.sy",
  "emailVerified": false,
  "createdAt": "2026-08-26T09:12:44Z"
}
```
A verification email is dispatched (Outbox → notification). Validation failures → `422` with the bilingual
`errors` array (§7.2). Duplicate email → `409` (`DUPLICATE_RESOURCE`).

#### `POST /auth/verify-email` — moves onboarding `Draft → EmailVerified`

Request:
```json
{ "email": "purchasing@nour-linens.sy", "token": "F3K9-2XQ7-88AB" }
```
Response `200 OK`:
```json
{ "supplierCode": "SUP-2026-004512", "onboardingState": "EmailVerified", "emailVerified": true }
```
Expired/invalid token → `422` (`VERIFICATION_TOKEN_INVALID`).

#### `POST /auth/login`

Request:
```json
{ "email": "purchasing@nour-linens.sy", "password": "••••••••••••" }
```
Response `200 OK` (refresh token set as HttpOnly cookie):
```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9…",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "user": {
    "userId": "usr_01J…opaque",
    "email": "purchasing@nour-linens.sy",
    "roles": ["supplier_admin"],
    "permissions": ["supplier.read", "supplier.update", "document.upload", "proposal.submit"],
    "supplierCode": "SUP-2026-004512",
    "locale": "ar",
    "mfaEnabled": false
  }
}
```
Bad credentials → `401` (`TOKEN_INVALID` / generic message, no account enumeration). Locked out → `429`.

#### `POST /auth/refresh` — rotating refresh

No body; sends the refresh cookie. Response `200 OK` returns a new `accessToken` (and rotates the refresh
cookie). Reused/rotated token → `401` (`TOKEN_INVALID`) **and** family revocation (theft response).

#### `POST /auth/logout`

Revokes the current refresh-token family; clears the cookie. `204 No Content`.

### 12.2 Supplier profile

#### `GET /suppliers/{supplierCode}` — profile (row-scoped)

Response `200 OK` (`ETag: "W/AAAAADk="`):
```jsonc
{
  "supplierCode": "SUP-2026-004512",
  "externalId": null,                 // set once synced to ERPNext Supplier
  "syncStatus": "NotSynced",
  "legalName": "شركة نور للمنسوجات",
  "displayName": "Nour Linens",
  "onboardingState": "ProfileInProgress",
  "legalInfo": {
    "taxId": "…",                     // generic; Syrian-specific rules [ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]
    "registrationNumber": "…"
  },
  "defaultCurrency": "SYP",
  "categories": ["hospitality-linens", "housekeeping-supplies"],
  "addresses": [ { "type": "HeadOffice", "city": "دمشق", "line1": "…" } ],
  "contacts": [ { "name": "ليان الأحمد", "email": "…", "phone": "…", "isPrimary": true } ],
  "documentsSummary": { "required": 5, "approved": 2, "pending": 2, "rejected": 1 },
  "profileCompleteness": 0.62,
  "updatedAt": "2026-08-26T09:40:11Z"
}
```

#### `PATCH /suppliers/{supplierCode}` — edit draft profile (JSON Merge Patch)

Requires `supplier.update` and `If-Match`. Request:
```json
{ "displayName": "Nour Linens Co.", "defaultCurrency": "SYP" }
```
Response `200 OK` with updated resource + new `ETag`. Stale `If-Match` → `412`. Editing a field not allowed
in the current onboarding state → `409` (`ILLEGAL_TRANSITION`) or `422` (`FIELD_LOCKED`).

#### `POST /suppliers/{supplierCode}/onboarding/submit` — `ProfileInProgress → Submitted`

Requires `supplier.submit`, `Idempotency-Key`, `If-Match`. `200 OK` returns the supplier with
`onboardingState: "Submitted"`. Incomplete required docs/fields → `422` listing exactly what is missing
(bilingual). Illegal source state → `409`.

### 12.3 Documents

#### `POST /suppliers/{supplierCode}/documents` — upload (`multipart/form-data`)

Parts: `file` (binary) + `metadata` (JSON: `{ "documentTypeCode": "COMMERCIAL_REGISTER", "issuedAt": "2026-01-15", "expiresAt": "2027-01-15" }`).
Response `202 Accepted` — `Location: /api/v1/suppliers/SUP-2026-004512/documents/DOC-2026-013377`:
```json
{
  "documentId": "DOC-2026-013377",
  "documentTypeCode": "COMMERCIAL_REGISTER",
  "state": "Uploaded",
  "fileName": "commercial-register.pdf",
  "sizeBytes": 184213,
  "scanStatus": "Pending",
  "uploadedAt": "2026-08-26T09:44:02Z"
}
```
Disallowed MIME → `415`; oversize → `413`; unknown `documentTypeCode` → `422`.

#### `GET /suppliers/{supplierCode}/documents` — list (page mode default for back-office)

`GET …/documents?state=UnderReview,Rejected&sort=-uploadedAt&page=1&pageSize=20`
```jsonc
{
  "data": [
    {
      "documentId": "DOC-2026-013377",
      "documentTypeCode": "COMMERCIAL_REGISTER",
      "state": "UnderReview",
      "expiresAt": "2027-01-15",
      "expiryState": "Valid",          // Valid | ExpiringSoon | Expired
      "downloadUrl": "/api/v1/documents/DOC-2026-013377/content",  // → 302 to short-lived pre-signed URL
      "uploadedAt": "2026-08-26T09:44:02Z"
    }
  ],
  "pagination": { "mode": "page", "page": 1, "pageSize": 20, "totalCount": 6, "hasMore": false },
  "meta": { "sort": "-uploadedAt", "filtersApplied": ["state=UnderReview,Rejected"] }
}
```
Reviewer transition: `POST …/documents/{documentId}/approve` / `…/reject` (requires `document.review`,
`reason` mandatory on reject) → moves `UnderReview → Approved | Rejected(reason)`.

### 12.4 RFQ

#### `GET /rfqs` — supplier-facing list of invited/published RFQs (cursor default)

`GET /rfqs?state=SubmissionOpen&category=hospitality-linens&sort=-publishedAt&pageSize=20`
```jsonc
{
  "data": [
    {
      "rfqCode": "RFQ-2026-000123",
      "title": "توريد مفروشات فندقية — الموسم الشتوي",
      "buyingOrg": { "code": "ORG-HTL-0007", "name": "Cham Palace Hotels" },
      "state": "SubmissionOpen",
      "publishedAt": "2026-08-20T08:00:00Z",
      "submissionDeadline": "2026-09-05T14:00:00Z",
      "itemsCount": 12,
      "invitationStatus": "Invited",     // for the calling supplier
      "hasDraftProposal": true
    }
  ],
  "pagination": { "mode": "cursor", "nextCursor": "eyJwdWJsaXNoZWRBdCI6…", "prevCursor": null, "pageSize": 20, "hasMore": true },
  "meta": { "sort": "-publishedAt", "filtersApplied": ["state=SubmissionOpen", "category=hospitality-linens"] }
}
```

#### `GET /rfqs/{rfqCode}` — detail (`?expand=items,requirements,clarifications`)

Response `200 OK` (`ETag` present) includes `items[]`, `requirements[]`, `attachments[]`, `timeline`
(publish/open/close/deadline), `evaluationTemplateRef`, and — for buyers — `invitations[]`. Fields visible
per persona are row-scoped (a supplier never sees other suppliers' proposals or the evaluation internals).

#### `POST /rfqs/{rfqCode}/publish` — `Approved → Published` (buyer)

Requires `rfq.publish`, `Idempotency-Key`, `If-Match`. `200 OK` → `state: "Published"` and emits invitation
notifications via Outbox. Wrong source state → `409` (`ILLEGAL_TRANSITION`, includes `allowedNext`).

### 12.5 Proposal

#### `POST /rfqs/{rfqCode}/proposals` — create draft (`Draft`), one per supplier per RFQ

Requires `proposal.create`; supplier must be `Invited` and RFQ in `SubmissionOpen`. Request:
```json
{ "currency": "SYP", "validityDays": 30 }
```
Response `201 Created` — `Location: /api/v1/proposals/PRO-2026-000891`:
```json
{ "proposalCode": "PRO-2026-000891", "rfqCode": "RFQ-2026-000123", "state": "Draft", "currency": "SYP", "createdAt": "2026-08-26T10:02:00Z" }
```
A second create by the same supplier → `409` (`DUPLICATE_RESOURCE`, links the existing draft).

#### `PATCH /proposals/{proposalCode}` — edit draft (line items, terms) with `If-Match`

Request (JSON Merge Patch):
```json
{
  "items": [
    { "rfqItemId": "RIT-001", "unitPrice": 45000, "quantity": 500, "leadTimeDays": 21 },
    { "rfqItemId": "RIT-002", "unitPrice": 12000, "quantity": 1200, "leadTimeDays": 14 }
  ],
  "commercialTerms": { "paymentTerms": "NET_30", "incoterm": "DAP" },
  "technicalResponse": "نستخدم أقمشة قطنية 300 خيط…"
}
```
Response `200 OK` with recomputed totals and new `ETag`. `unitPrice ≤ 0` → `422`
(`PRICE_NON_POSITIVE`, field `items[0].unitPrice`, bilingual). Editing after `Submitted` → `409`.

#### `POST /proposals/{proposalCode}/submit` — `Draft → Submitted`

Requires `proposal.submit`, **`Idempotency-Key` (required)**, `If-Match`. Response `200 OK`:
```json
{
  "proposalCode": "PRO-2026-000891",
  "state": "Submitted",
  "submittedAt": "2026-08-26T10:18:33Z",
  "totals": { "currency": "SYP", "grandTotal": 36900000 }
}
```
- Missing line items → `422` (`PROPOSAL_ITEMS_REQUIRED`).
- After the RFQ `submissionDeadline` → `409` (`SUBMISSION_WINDOW_CLOSED`).
- Double-click / network retry with the same `Idempotency-Key` → the original `200` replayed
  (`Idempotency-Replayed: true`) — **no** duplicate submission.

Supplier withdrawal while `SubmissionOpen`: `POST /proposals/{proposalCode}/withdraw` → `Withdrawn`
(requires `reason`; audited).

---

## 13. Cross-cutting conformance checklist (per slice "definition of done")

- [ ] Public ids only in paths/bodies (no GUID/int leakage).
- [ ] List endpoint uses the standard envelope + declared pagination mode.
- [ ] All errors are RFC 9457 `problem+json` with `type`, `code`, `traceId`, `correlationId`.
- [ ] Validation returns the bilingual field `errors` array.
- [ ] Mutations require `If-Match`; unsafe POSTs honor `Idempotency-Key` (required on submit/approve/publish).
- [ ] Correlation-Id echoed and propagated to logs/audit/outbox.
- [ ] Permission (`resource.action`) enforced at the API + row-scope in the query layer.
- [ ] State transitions are named POST sub-resources; illegal transitions → `409` with `allowedNext`.
- [ ] Endpoint documented in OpenAPI (permission, schemas, error `type`s, headers) and visible in Scalar.
- [ ] Rate-limit policy assigned; upload endpoints enforce MIME/size.
- [ ] `Accept-Language` honored for localized messages; RTL-agnostic (API returns keys/text, not layout).

---

### Related documents

- [`../architecture/00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) — canonical stack, personas, state machines, RBAC, tokens.
- [`../product/DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md) — ERP integration surface & gaps.
- [`../architecture/DOMAIN-MODEL.md`](../architecture/DOMAIN-MODEL.md) — aggregates, VOs, `RowVersion`, `ExternalId` (to be authored).
- [`../product/BUSINESS-PROCESSES.md`](../product/BUSINESS-PROCESSES.md) — authoritative state machines (to be authored).
- [`../integration/`](../integration/) — ACL + Outbox + ERPNext adapter contracts (to be authored).
- [`../security/`](../security/) — OWASP ASVS L2, token handling, audit (to be authored).
- [`../product/ASSUMPTIONS.md`](../product/ASSUMPTIONS.md) · [`../product/OPEN-QUESTIONS.md`](../product/OPEN-QUESTIONS.md) — tracked `[ASSUMPTION]` items.
