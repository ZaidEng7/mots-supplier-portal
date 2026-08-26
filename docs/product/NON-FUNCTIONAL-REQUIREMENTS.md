# Non-Functional Requirements — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Principal Architect · **Date:** 2026-08-26
> **Canonical sources (must remain consistent):**
> [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](./DISCOVERY-REPORT.md)
> **Related:** [`FUNCTIONAL-REQUIREMENTS.md`](./FUNCTIONAL-REQUIREMENTS.md) ·
> `../ux/DESIGN-SYSTEM.md` · `../security/` · `../deployment/` · [`../integration/`](../integration/)

---

## How to read this document

- Each requirement has a stable ID `NFR-###`, a **concrete, testable target**, and a
  **verification** method. Targets in the canonical brief §9 are the **baseline** and are marked
  *(canonical)*; expansions are consistent with that baseline.
- **Verification** vocabulary: *Load test* (k6/NBomber), *Synthetic monitor*, *Lighthouse/CI*,
  *axe-core/Playwright a11y*, *Unit/Integration test* (xUnit/Vitest), *NetArchTest* (architecture
  rules), *Pen test / ASVS review*, *Chaos/failover drill*, *DR restore drill*, *Manual audit*.
- Syria-specific legal/tax/regulatory rules are **not invented**; where an NFR would depend on one it
  is tagged **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** and mirrored in `ASSUMPTIONS.md`.

---

## 1. Performance

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-PERF-001 | **API p95 < 300ms for reads** under expected peak load *(canonical)*. | Load test against read endpoints; assert p95 in CI perf gate. |
| NFR-PERF-002 | **API p95 < 800ms for writes** under expected peak load *(canonical)*. | Load test on write flows (submit proposal, transition state). |
| NFR-PERF-003 | **Web LCP < 2.5s** on mid-range mobile over 4G *(canonical)*. | Lighthouse CI on key routes (login, dashboard, RFQ, proposal). |
| NFR-PERF-004 | **INP < 200ms** on mid-range mobile *(canonical)*. | Lighthouse/RUM; interaction tracing on forms and tables. |
| NFR-PERF-005 | **Route-level code splitting** *(canonical)*; initial JS for a first paint route ≤ 250KB gzipped (target). | Bundle-size budget check in CI (Vite build report). |
| NFR-PERF-006 | List/table endpoints are **paginated server-side**; no unbounded queries; default page ≤ 50 rows. | Integration test asserting pagination + query plans. |
| NFR-PERF-007 | Database queries on hot paths use appropriate indexes; N+1 access is prohibited on list views. | EF Core query logging review; integration test count-of-queries. |
| NFR-PERF-008 | File upload/download streams (no full-buffer in memory); supports files up to a configurable max (default 20MB). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** max size. | Load/soak test on `IFileStorage` paths. |
| NFR-PERF-009 | Background jobs (Hangfire) do not degrade interactive API p95; heavy work is off the request path. | Load test with concurrent job execution. |
| NFR-PERF-010 | Server-side response payloads are compressed; static assets are cache-headered and fingerprinted. | Synthetic check of headers; Lighthouse. |

## 2. Scalability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-SCAL-001 | API is **stateless** (JWT-based) and **horizontally scalable** behind a load balancer; no in-process session affinity required. | Multi-instance load test; kill-one-instance drill. |
| NFR-SCAL-002 | Baseline capacity target: **≥ 500 concurrent authenticated users** and **≥ 50 req/s sustained** per instance without breaching PERF SLOs. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** volumes. | Load test to target; publish headroom curve. |
| NFR-SCAL-003 | PostgreSQL uses connection pooling; connection counts stay within limits under peak. | Load test with pool metrics. |
| NFR-SCAL-004 | Background processing scales independently of the API (separate worker capacity for jobs/Outbox). | Scale-out drill of workers under queue backlog. |
| NFR-SCAL-005 | File storage scales via S3-compatible object storage in prod (MinIO/S3), decoupled from app nodes. | Prod-like storage load test. |
| NFR-SCAL-006 | Data model and queries remain performant with growth targets (e.g. 100k suppliers, 10k RFQs, 100k proposals). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | Seeded-volume performance test. |

## 3. Availability & Reliability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-AVL-001 | **Portal availability 99.5%** (monthly), measured independently of ERP *(canonical)*. | Synthetic uptime monitor + SLO reporting. |
| NFR-AVL-002 | The portal **operates fully without ERP availability**; no core flow blocks on ERP *(canonical)*. | Chaos drill: ERP adapter down; verify registration→award succeed. |
| NFR-AVL-003 | Integration is **async via Outbox**; ERP outages queue and drain without data loss. | Fault-injection: stop ERP, confirm Outbox retry + drain. |
| NFR-AVL-004 | Graceful degradation: non-critical features (e.g. email, ERP sync) failing does not break core UX; user sees clear localized status. | Chaos test on email/ERP subsystems. |
| NFR-AVL-005 | Health/readiness endpoints exist for liveness/readiness probes; unhealthy instances are removed from rotation. | Probe test in staging; failover drill. |
| NFR-AVL-006 | At-least-once delivery for domain/integration events with **idempotent consumers**; no duplicate side effects on retry. | Integration test replaying Outbox messages. |
| NFR-AVL-007 | Optimistic concurrency via **`RowVersion`** prevents lost updates on concurrent edits. | Concurrency integration test (parallel writes conflict). |
| NFR-AVL-008 | Planned maintenance uses rolling deploys with zero-downtime target for read paths. | Deploy drill measuring request errors during rollout. |

## 4. Security

Baseline: **OWASP ASVS L2** targets *(canonical)*; audit for all state changes *(canonical)*.

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-SEC-001 | Meet **OWASP ASVS Level 2** controls across auth, session, access control, validation, and data handling. | ASVS L2 checklist review + pen test. |
| NFR-SEC-002 | Authentication uses ASP.NET Core Identity with **JWT access + rotating refresh tokens**; refresh reuse invalidates the token family. | Security test of token rotation/replay. |
| NFR-SEC-003 | **MFA-ready** (TOTP) with enforceability per role; enforced for `system_admin` (and high-privilege roles per policy). | Functional + policy test. |
| NFR-SEC-004 | Authorization is **deny-by-default, policy-based on permission claims**; every endpoint declares required `resource.action`; **row-scoping** enforced server-side. | NetArchTest + integration authz tests per persona. |
| NFR-SEC-005 | All input validated (FluentValidation server + Zod client); output encoding prevents XSS; parameterized queries (EF Core) prevent SQL injection. | SAST + integration tests; pen test. |
| NFR-SEC-006 | Transport is **TLS 1.2+ only**; HSTS enabled; secure/HttpOnly/SameSite cookies where cookies are used. | TLS scan; header synthetic check. |
| NFR-SEC-007 | Secrets are never in source; managed via environment/secret store; no secrets in logs. | Secret-scanning in CI; log review. |
| NFR-SEC-008 | Uploaded files are **malware-scanned**, type/MIME/size validated, stored outside web root, and served only via authorized, time-limited access. | Integration test + pen test on upload/download. |
| NFR-SEC-009 | Rate limiting and lockout on auth endpoints; bot/abuse protections on registration. | Load/abuse test. |
| NFR-SEC-010 | Security headers (CSP, X-Content-Type-Options, Referrer-Policy, frame-ancestors) are applied. | Synthetic header check; ZAP baseline. |
| NFR-SEC-011 | Dependencies are scanned for known vulnerabilities; builds fail on high/critical. | SCA (Dependabot/`dotnet`/npm audit) in CI. |
| NFR-SEC-012 | **UI is never trusted** for authorization; server re-enforces every permission and scope. | Integration test hitting endpoints without UI affordance. |
| NFR-SEC-013 | Sensitive actions (award, approvals, admin, exports) require appropriate permission and are audited with correlationId. | Audit-coverage integration test. |

## 5. Privacy & Data Protection

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-PRIV-001 | Personal and commercial data is access-controlled by role and scope; suppliers' commercial proposal data is not exposed to peers. | Integration test of cross-scope isolation. |
| NFR-PRIV-002 | **Data minimization:** only fields needed for procurement are collected; Syria-specific legal/tax fields captured generically. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | Data-map review vs. field list. |
| NFR-PRIV-003 | **Ministry read-only** access is limited to permitted metrics; visibility of commercial values vs. aggregate/anonymized is configurable. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | Access-scope test for `ministry_viewer`. |
| NFR-PRIV-004 | Personal/sensitive data is **never placed in URLs, query strings, or logs**; PII is redacted in structured logs. | Log-scrub test; route/query review. |
| NFR-PRIV-005 | Data is encrypted **in transit (TLS)** and **at rest** (DB + object storage encryption). | Config audit; storage encryption verification. |
| NFR-PRIV-006 | **Retention policy** for abandoned drafts, expired tokens, and lifecycle data with audited cleanup jobs; soft-delete only where lifecycle demands, otherwise hard delete + audit *(canonical)*. | Retention-job integration test + audit check. |
| NFR-PRIV-007 | Data subject / commercial confidentiality handling (export, correction, restriction) is supported operationally. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** applicable regime. | Process review. |

## 6. Accessibility (WCAG 2.2 AA)

Baseline: **WCAG 2.2 AA** *(canonical)*.

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-A11Y-001 | All pages/components meet **WCAG 2.2 Level AA**. | Automated **axe-core** in Playwright + manual audit. |
| NFR-A11Y-002 | Full keyboard operability with visible focus order that follows RTL/LTR reading direction; no keyboard traps. | Keyboard-only test pass. |
| NFR-A11Y-003 | Color contrast ≥ 4.5:1 for text and ≥ 3:1 for UI/graphics, validated for both AR and EN themes and both light usage of tokens. | Contrast check against design tokens. |
| NFR-A11Y-004 | Semantic structure and ARIA on interactive components (built on Radix primitives); proper roles/labels on forms, tables, dialogs, notifications. | axe-core + screen-reader spot check (NVDA/VoiceOver). |
| NFR-A11Y-005 | Screen-reader support in **Arabic and English**, with correct language and direction attributes (`lang`, `dir`). | Screen-reader test in both locales. |
| NFR-A11Y-006 | Respect **`prefers-reduced-motion`**; motion is 120–200ms ease-out and non-essential *(canonical tokens)*. | Manual + automated motion check. |
| NFR-A11Y-007 | Targets are adequately sized; errors are announced and programmatically associated with fields. | Component a11y tests (RTL/axe). |
| NFR-A11Y-008 | Accessibility is gated in CI: a11y test failures block merge on covered routes. | CI a11y gate. |

## 7. Localization & RTL

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-L10N-001 | **Arabic-first** (`ar`, RTL) default with English (`en`, LTR) secondary; **every string keyed via i18next** *(canonical)* — no hard-coded UI strings. | Lint rule / test for missing keys; pseudo-loc test. |
| NFR-L10N-002 | Layout uses **CSS logical properties** and `dir` switching; no hard-coded left/right; verified in both directions. | Visual RTL/LTR regression (Playwright/Storybook). |
| NFR-L10N-003 | Directional icons **mirror under RTL** (Lucide) *(canonical)*. | Storybook RTL visual check. |
| NFR-L10N-004 | Currency default **SYP**, configurable; **multi-currency proposals** with display currency *(canonical)*. | Functional test of currency formatting/selection. |
| NFR-L10N-005 | Dates default **Gregorian**, locale-aware formatting; **[ASSUMPTION]** Hijri display optional/future *(canonical)*. | Locale formatting test. |
| NFR-L10N-006 | Numerals default **Western Arabic (0–9)**, configurable to Eastern Arabic; tabular figures for tables/prices *(canonical, [ASSUMPTION])*. | Numeral config test. |
| NFR-L10N-007 | Fonts: **IBM Plex Sans Arabic** (Arabic) + **Inter** (Latin/numerals) *(canonical)*; both self-hosted, no layout shift on load. | Visual + font-loading check. |
| NFR-L10N-008 | Email/notification templates are localized (AR/EN) to recipient locale. | Template render test per locale. |

## 8. Usability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-USE-001 | **Premium, calm, trustworthy** UX consistent with the evergreen-teal/warm-stone/gold design tokens *(canonical)*; not a template look (no MUI/AntD/Bootstrap). | Design review vs. `DESIGN-SYSTEM.md`. |
| NFR-USE-002 | Multi-step flows (onboarding, RFQ, proposal, evaluation) show clear progress, current state, next actions, and blockers. | Usability review + task-completion test. |
| NFR-USE-003 | **Draft safety:** long forms (proposal, onboarding) never lose work; auto-persist and recoverable drafts. | Functional test simulating navigation/loss. |
| NFR-USE-004 | Errors are actionable, localized, and field-associated; destructive/irreversible actions require confirmation with clear consequences. | Component + E2E tests. |
| NFR-USE-005 | Responsive across mobile→desktop; supplier surfaces are mobile-capable, back-office desktop-optimized (per persona device profile). | Responsive visual tests at breakpoints. |
| NFR-USE-006 | Consistent components from the bespoke design system (Storybook), ensuring interaction consistency across the app. | Storybook coverage + visual QA. |
| NFR-USE-007 | Empty, loading, and error states are designed for every data view (skeletons, not spinners where feasible). | Component review. |

## 9. Maintainability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-MNT-001 | **Clean Architecture + Vertical Slice** layering (Api/Application/Domain/Infrastructure) enforced; Domain has no outward dependencies. | **NetArchTest** rules in CI. |
| NFR-MNT-002 | Command/query dispatch via **direct handler classes** (no MediatR); one slice per feature. | Architecture test + review. |
| NFR-MNT-003 | Automated test coverage targets: Domain/Application ≥ 80% line coverage; critical flows have integration + E2E tests. **[ASSUMPTION]** threshold. | Coverage gate in CI. |
| NFR-MNT-004 | Real-DB integration tests via **Testcontainers (Postgres)** and **WebApplicationFactory**; no mocked persistence for integration suites. | CI test run. |
| NFR-MNT-005 | Compile-time mapping (**Mapperly**) and source-generated code; no reflection-heavy mappers. | Build check; review. |
| NFR-MNT-006 | Consistent code style enforced (analyzers/formatters for .NET; ESLint/Prettier for TS); builds fail on lint/analyzer errors. | CI lint gate. |
| NFR-MNT-007 | Public references use **opaque slugs/short codes**; internal integer/GUIDv7 PKs never exposed in URLs *(canonical)*. | Route/contract review + test. |
| NFR-MNT-008 | Database changes are **versioned migrations** (EF Core); no manual schema drift. | Migration check in CI. |
| NFR-MNT-009 | Frontend/back-end share validation intent (Zod client / FluentValidation server) to avoid divergence. | Contract test. |

## 10. Observability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-OBS-001 | **Structured JSON logging (Serilog)** with correlation IDs on every request and domain event. | Log-schema test; trace-through check. |
| NFR-OBS-002 | **OpenTelemetry** traces, metrics, and logs exported vendor-neutrally; end-to-end trace across API → job → Outbox → ERP adapter. | Trace inspection in staging. |
| NFR-OBS-003 | **correlationId** links AuditLog entries to distributed traces for investigation *(canonical audit fields)*. | Cross-reference test. |
| NFR-OBS-004 | Key business & system metrics are emitted (RFQ throughput, submission counts, Outbox lag, job failures, auth failures). | Metrics presence test; dashboard review. |
| NFR-OBS-005 | Alerting on SLO breaches, Outbox/dead-letter growth, job failures, and error-rate spikes. | Alert-rule review + synthetic trigger. |
| NFR-OBS-006 | Health of background jobs (Hangfire) and integration (Outbox) is observable to admins. | Admin dashboard verification. |
| NFR-OBS-007 | Logs never contain secrets or unredacted PII (see NFR-PRIV-004). | Log-scrub test. |

## 11. Compliance & Auditability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-CMP-001 | **Every state change is audited** with actor, timestamp, from→to, reason, correlationId *(canonical)*. | Audit-coverage integration test across state machines. |
| NFR-CMP-002 | AuditLog is **append-only/immutable**; no edit/delete path exists, including for admins. | Attempt-to-mutate negative test. |
| NFR-CMP-003 | **Illegal state transitions are rejected by the domain**, not just the UI *(canonical)*. | Domain unit tests per state machine. |
| NFR-CMP-004 | Audit records are queryable/exportable for governance oversight (Ministry). | Export test; scope test. |
| NFR-CMP-005 | Document access, downloads, and data exports are recorded as audit events. | Access-audit test. |
| NFR-CMP-006 | Syrian legal/tax/regulatory compliance rules are **not invented**; compliance-dependent fields are generic and flagged. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | Field-tag review vs. `ASSUMPTIONS.md`. |
| NFR-CMP-007 | Retention and immutability satisfy procurement governance expectations (defensible award file retained). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | Governance review. |

## 12. Portability

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-PORT-001 | App runs in **containers** and is cloud/on-prem portable; no single-cloud lock-in for core runtime. | Build/run in container in CI + staging. |
| NFR-PORT-002 | **File storage is provider-independent** via `IFileStorage` (local disk dev / S3-compatible prod) *(canonical §23 requirement)*. | Swap-provider integration test (local ↔ MinIO). |
| NFR-PORT-003 | **Identity provider is swappable** to external IdP (Keycloak/Entra) without changing authorization semantics *(canonical)*. | Adapter/contract test. |
| NFR-PORT-004 | Configuration is environment-driven (12-factor); no environment-specific code branches. | Config audit across environments. |
| NFR-PORT-005 | Database is standard PostgreSQL 17 (Npgsql/EF Core); no proprietary extensions that block portability. | Schema/dependency review. |

## 13. Interoperability & Integration

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-INT-001 | ERP integration is **async-by-default** through an **ACL + transactional Outbox + adapters**; the portal never blocks core flows on ERP *(canonical)*. | Chaos drill (ERP down) + Outbox drain test. |
| NFR-INT-002 | Every ERP-synced entity carries a nullable **`ExternalId` (string)**; **never an integer FK** to ERP *(canonical)*. | Schema review + contract test. |
| NFR-INT-003 | Sync metadata (`SyncStatus`, `LastSyncedAt`, `RowVersion`) is maintained; conflicts are queued, never silently overwritten. | Conflict-injection test. |
| NFR-INT-004 | Outbox delivery is **at-least-once with idempotent, retrying, dead-lettering** consumers. | Replay + duplicate-suppression test. |
| NFR-INT-005 | Contracts align to ERPNext `buying` doctypes (Supplier, RFQ(+Supplier), Supplier Quotation, Scorecard) at the **contract** level, not code reuse *(canonical/discovery)*. | Mapping/contract review. |
| NFR-INT-006 | API is documented via **native .NET OpenAPI + Scalar**; contracts are versioned and backward-compatible within a major version. | OpenAPI diff check in CI. |
| NFR-INT-007 | Adapters are versioned and swappable; contract mismatches **fail safe** (dead-letter + alert), not silently. | Fault-injection test. |

## 14. Backup & Disaster Recovery

Baseline: **Backups + PITR** *(canonical)*.

| ID | Requirement & target | Verification |
|---|---|---|
| NFR-DR-001 | PostgreSQL has automated backups with **Point-In-Time Recovery (PITR)** *(canonical)*. | Restore-to-timestamp drill. |
| NFR-DR-002 | Object storage (documents) is backed up/replicated; document loss is prevented. | Storage restore drill. |
| NFR-DR-003 | **RPO ≤ 15 minutes** and **RTO ≤ 4 hours** for the core portal. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** targets. | DR restore drill measuring RPO/RTO. |
| NFR-DR-004 | Backups are **encrypted** and access-controlled; restore procedures are documented and tested. | Encryption check + documented drill. |
| NFR-DR-005 | Outbox/integration state survives failover so pending ERP syncs are not lost during recovery. | Failover + drain verification. |
| NFR-DR-006 | DR runbooks exist and are exercised on a scheduled cadence. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** cadence. | Runbook review + drill log. |

---

## Traceability & governance

- NFR baselines trace directly to canonical brief **§9 (Non-functional targets)**, **§7 (Design
  tokens)**, **§8 (Localization)**, **§6 (RBAC)**, **§1 & §4 (ERP boundary / aggregates)**, and the
  technology decisions in **§2**.
- Functional behaviors that these NFRs qualify are specified in
  [`FUNCTIONAL-REQUIREMENTS.md`](./FUNCTIONAL-REQUIREMENTS.md).
- All **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** items are mirrored in `ASSUMPTIONS.md` and
  must be resolved before the dependent NFR target is treated as contractually binding.
- NFR verification gates (perf budgets, a11y, architecture rules, security/SCA scans, coverage) are
  wired into CI so regressions block merge, consistent with the vertical-slice "done" definition.
