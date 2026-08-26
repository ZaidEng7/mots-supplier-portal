# MOTS Supplier Portal — Foundational Decisions (Canonical Brief)

> **Status:** Baseline v1 · **Owner:** Principal Architect · **Date:** 2026-08-26
> This is the **single source of truth** that all product, UX, architecture, and backlog
> documents must remain consistent with. Where a decision is not yet business-confirmed it is
> tagged `[ASSUMPTION]` and mirrored in [`docs/product/ASSUMPTIONS.md`](../product/ASSUMPTIONS.md).

---

## 0. Context in one paragraph

MOTS Supplier Portal is a **standalone, independently deployable** external portal for the Syrian
tourism sector. Suppliers register and onboard, buying entities (hotels / MOT-affiliated
organizations) publish RFQs and invite suppliers, suppliers submit proposals, committees evaluate
against configurable weighted criteria, and awards are made. The **Ministry of Tourism** monitors
the ecosystem. The portal will **eventually integrate** with an existing **ERPNext (Frappe)** ERP
which is the long-term **system of record for approved supplier master data and purchase orders** —
but the portal must operate fully without the ERP being available.

## 1. ERP boundary (non-negotiable)

- The ERP at `/Users/issamshadid/Repos/ERP` is **ERPNext** (Frappe framework, Python, MariaDB).
  We deliberately **do not** reuse its stack, patterns, schema, or UI.
- ERPNext `buying` domain we align *contracts* to (not code): `Supplier`, `Request for Quotation`,
  `Request for Quotation Supplier`, `Supplier Quotation`, `Supplier Scorecard` (+ criteria/period).
- ERPNext identifiers are **string naming-series keys** (e.g. `SUP-.YYYY.-00001`). Therefore every
  ERP-synced portal entity carries a nullable **`ExternalId` (string)**, never an integer FK to ERP.
- **Source-of-truth split:**
  - Portal owns: registration, onboarding, documents, RFQ authoring, invitations, proposals,
    clarifications, evaluation, recommendation, award decision, notifications, audit.
  - ERP owns (post-integration): approved **Supplier master**, **Purchase Orders**, financial
    postings, tax/accounting reference data.
- Integration is **async-by-default** through an Anti-Corruption Layer (ACL) + transactional
  **Outbox** + adapters. The portal never blocks core flows on ERP availability. See
  [`docs/integration/`](../integration/).

## 2. Technology decisions & justifications

### Backend
| Area | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS), C# 14** | Latest LTS at build time (Nov 2025); long support window, top perf. |
| API | **ASP.NET Core Minimal APIs**, feature-grouped | Thin endpoints; no controller bloat. |
| Architecture | **Clean Architecture + Vertical Slice** (Api / Application / Domain / Infrastructure) | Testable core, feature cohesion; avoids generic-repo/god-service anti-patterns. |
| Command/query dispatch | **Direct handler classes** per slice (no MediatR) | MediatR is now commercially licensed; a thin DI-resolved dispatcher avoids that and needless indirection. |
| Persistence | **EF Core 10 + PostgreSQL 17** (Npgsql) | Mature relational fit for procurement; strong migrations, JSONB for flexible offerings. |
| Validation | **FluentValidation** | Expressive, testable; wired into the request pipeline. |
| Mapping | **Mapperly** (source-generated) | Compile-time, zero-reflection; avoids AutoMapper licensing. |
| AuthN | **ASP.NET Core Identity** + **JWT access + rotating refresh tokens** | Local identity now; **MFA-ready** (Identity 2FA); swappable for external IdP (Keycloak/Entra) later. |
| AuthZ | **Policy-based + permission claims (RBAC)** | Fine-grained `resource.action` permissions mapped to roles; row-scoping by Supplier/Org. |
| API docs | **Native .NET OpenAPI** + **Scalar** UI | Swashbuckle is fading post-.NET 9; native + Scalar is modern and clean. |
| Background jobs | **Hangfire (Postgres storage)** for scheduled/recurring + **Outbox** for domain/integration events | Durable retries, dashboard, dead-letter; transactional consistency for events. |
| Logging/telemetry | **Serilog (JSON) + OpenTelemetry** (traces/metrics/logs) | Structured logs, correlation IDs, vendor-neutral export. |
| File storage | **`IFileStorage` abstraction**: local disk (dev) / **S3-compatible (MinIO/prod)** | Storage-provider independence per requirement §23. |
| Testing | **xUnit + FluentAssertions + Testcontainers (Postgres) + WebApplicationFactory + NetArchTest** | Real-DB integration tests; architecture-rule enforcement. |

### Frontend
| Area | Choice | Why |
|---|---|---|
| Framework | **React 19 + TypeScript 5.7+** | Latest stable; concurrent features; strong typing. |
| Build | **Vite 7** | Fast dev/build, first-class TS/ESM. |
| Routing | **TanStack Router** | End-to-end type-safe routes, search-param validation, data loaders. |
| Server state | **TanStack Query** | Caching, background refetch, optimistic updates, request dedup. |
| Client state | **Zustand** | Minimal global state (session/UI); avoids Redux over-engineering. |
| Forms | **React Hook Form + Zod** | Performant forms; Zod schemas shared for client+contract validation. |
| Design system | **Tailwind CSS v4 + Radix UI primitives → bespoke component library** | Full control to hit a premium, non-template look; a11y from Radix; **NOT** MUI/AntD/Bootstrap (those read as templates). |
| i18n / RTL | **i18next + react-i18next**, **CSS logical properties**, `dir` switching | Arabic-first, English secondary; RTL is designed-in, not bolted-on. |
| Data viz | **Recharts** + custom SVG for premium moments | Composable/themeable; bespoke where polish matters. |
| Tables | **TanStack Table** (headless) styled by our DS | Full control of responsive + RTL behavior. |
| Icons | **Lucide** with directional mirroring under RTL | Clean, consistent, open. |
| Fonts | **IBM Plex Sans Arabic** (Arabic) + **Inter** (Latin/numerals) | Both open-source, professional, script-harmonious. |
| Testing | **Vitest + React Testing Library + Playwright + axe-core** | Unit→component→E2E + automated a11y. |
| Workshop | **Storybook** | Design-system component development & visual QA. |

## 3. Canonical personas (see `docs/product/PERSONAS.md` for full)

| Key | Persona | Surface | Primary device |
|---|---|---|---|
| `supplier_admin` | Supplier Admin (primary representative) | Supplier app | Mobile + desktop |
| `supplier_user` | Supplier User (delegated representative) | Supplier app | Mobile + desktop |
| `onboarding_reviewer` | Supplier Onboarding / Compliance Reviewer | Back-office | Desktop |
| `procurement_officer` | Procurement Officer (buying entity) | Back-office | Desktop |
| `procurement_manager` | Procurement Manager / Approver | Back-office | Desktop |
| `evaluator` | Evaluation Committee Member | Back-office | Desktop/tablet |
| `ministry_viewer` | Ministry of Tourism Analyst/Supervisor (read-only governance) | Governance | Desktop |
| `system_admin` | System Administrator | Admin | Desktop |

## 4. Core domain — aggregates & boundaries (see `docs/architecture/DOMAIN-MODEL.md`)

Aggregate roots (transactional consistency boundaries) in **bold**; entities/value objects nested.

- **User** · Role · Permission · (membership to Organization or Supplier)
- **Organization** (buying entity: Hotel / MOT body / Ministry) · OrgUnit
- **Supplier** — `ExternalId?`, SupplierProfile, LegalInfo(VO), Address[], Contact[], Representative[],
  Branch[], BankAccount[], CategoryLink[], **Offering**[], **SupplierDocument**[], `OnboardingState`
- **RFQ** — RfqItem[], Requirement[], Attachment[], **Invitation**[], Clarification[] (Q&A),
  `EvaluationTemplateRef`, Timeline, `RfqState`
- **Proposal** — ProposalItem[] (line pricing), ProposalDocument[], CommercialTerms(VO),
  TechnicalResponse, Validity, `ProposalState` (one per Supplier per RFQ)
- **EvaluationTemplate** — Criterion[] (name, weight, max, threshold, scoring type)
- **Evaluation** — EvaluationAssignment[], EvaluatorScore[], ConsolidatedResult, `EvaluationState`
- **Award** — Recommendation, Approval[], AwardDecision, `ExternalPurchaseOrderRef?`, `AwardState`
- **Notification** · **AuditLog** · **Document** (shared abstraction) · **OutboxMessage**
- Reference data: **Category** (tree), **DocumentType**, Currency, UnitOfMeasure, Incoterm, Region

Every ERP-syncable aggregate carries: `ExternalId (string?)`, `SyncStatus`, `LastSyncedAt`,
`RowVersion` (concurrency). Internal PKs are **GUIDv7**; public references use opaque slugs/short
codes (e.g. `RFQ-2026-000123`) — internal integer/GUID PKs are never exposed in URLs.

## 5. Canonical state machines (authoritative — see `docs/product/BUSINESS-PROCESSES.md`)

**Supplier onboarding:** `Draft → EmailVerified → ProfileInProgress → Submitted → UnderReview →
(InfoRequested → Resubmitted → UnderReview)* → Approved | Rejected`; post-approval lifecycle
`Active ↔ Suspended → Deactivated`.

**Supplier document:** `Required → Uploaded → UnderReview → Approved | Rejected(reason)`; time-based
`Approved → ExpiringSoon → Expired`. Rejected/Expired ⇒ profile flagged incomplete.

**RFQ:** `Draft → InternalReview → Approved → Published → SubmissionOpen → SubmissionClosed →
UnderEvaluation → Clarification* → Shortlisting → Recommendation → AwardApproval → Awarded →
Completed`; `Cancelled` reachable from any pre-Awarded state (with reason + audit).

**Proposal:** `Draft → Submitted → UnderReview → (ClarificationRequested → Revised → UnderReview)* →
Shortlisted | NotSelected → AwardOffered → Awarded | Declined`; supplier-initiated `Withdrawn`
allowed while SubmissionOpen.

**Evaluation:** `NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated → Finalized`.
`[ASSUMPTION]` evaluators score **independently** (blind to peers) before consolidation.

**Award/Approval:** `Recommended → PendingApproval → Approved | Rejected → Awarded → (Outbox → ERP PO)`.

All transitions: recorded in **AuditLog** (actor, timestamp, from→to, reason, correlationId) and are
**permission-guarded**. Illegal transitions are rejected by the domain, not just the UI.

## 6. RBAC model

- **Permission** = `resource.action` (e.g. `supplier.approve`, `rfq.publish`, `proposal.submit`,
  `evaluation.score`, `award.approve`, `admin.users.manage`, `audit.read`).
- **Role** = named set of permissions (seeded defaults per persona; admin-editable).
- **Scoping:** suppliers see only their own `SupplierId`; procurement/evaluators scoped to their
  `OrganizationId`; ministry has **read-only** cross-organization aggregate access; admin is global.
- Enforced at API (policy handlers) **and** re-checked in UI for affordance-hiding only (never trust UI).

## 7. Design system tokens (canonical — see `docs/ux/DESIGN-SYSTEM.md`)

**Brand direction:** trustworthy, calm, premium — a deep **evergreen-teal** primary (heritage/tourism,
distinct from generic SaaS blue) with **warm stone** neutrals and a restrained **gold** accent.

```
--brand-50 #ECF6F3  --brand-100 #D2EBE4  --brand-200 #A6D6C9  --brand-300 #6FBAA8
--brand-400 #3E9A85  --brand-500 #1F8069  --brand-600 #136A57  brand primary
--brand-700 #0F5647  --brand-800 #0D453A  --brand-900 #0A3730
--accent-gold-500 #C8A045   (sparingly: highlights, awards, KPIs)
neutrals (warm stone): --n-50 #FAF9F7 … --n-900 #1C1B19
semantic: success #1E874B  warning #B7791F  danger #C0392B  info #2563A6
```
- **Type:** Inter (Latin/number), IBM Plex Sans Arabic (Arabic). Scale (rem): 12/13/14/16/18/20/24/30/36.
  Body 14–16; numeric tabular figures for tables/prices.
- **Spacing:** 4px base grid (`0 2 4 8 12 16 20 24 32 40 48 64`).
- **Radius:** inputs/buttons 8px; cards 12–16px; pills full.
- **Shadow:** soft layered elevation (`sm/md/lg`), never harsh; borders `1px n-200`.
- **Motion:** 120–200ms ease-out; respect `prefers-reduced-motion`.
- **RTL:** all spacing via logical properties; icons that imply direction mirror; numerals configurable.
- **Numerals:** Western Arabic digits (0–9) default, `[ASSUMPTION]` for Syrian business context; configurable to Eastern Arabic.

## 8. Localization

- **Arabic-first** (default `ar`, RTL), English (`en`, LTR) secondary. Every string keyed via i18next.
- Currency default **SYP (Syrian Pound)**, configurable; multi-currency proposals with display currency.
- Dates: Gregorian default; formatting locale-aware. `[ASSUMPTION]` Hijri display optional/future.
- **No invented Syrian legal/tax rules** — such fields are captured generically and flagged in ASSUMPTIONS.

## 9. Non-functional targets

- **Availability** 99.5% portal (ERP-independent). **API p95** < 300ms reads / < 800ms writes.
- **Web perf** LCP < 2.5s, INP < 200ms on mid-range mobile; route-level code splitting.
- **Accessibility** WCAG 2.2 AA. **Security** OWASP ASVS L2 targets; audit for all state changes.
- **Data** soft-delete only where lifecycle demands; otherwise hard delete + audit. Backups + PITR.

## 10. Repository layout

```
docs/{product,ux,architecture,api,security,deployment,backlog,integration,adr}
src/{backend,frontend}   tests/{backend,frontend,e2e}   infrastructure/  scripts/  .github/
```

## 11. Delivery approach

Vertical slices (UI+API+DB+validation+authz+tests per slice), phased roadmap Phase 0→12 in
[`docs/backlog/ROADMAP.md`](../backlog/ROADMAP.md). UX quality and business correctness gate "done".
