# Assumptions Register — MOTS Supplier Portal

> **Status:** Baseline v1 · **Date:** 2026-08-26 · **Owner:** Product / Principal Architect
> Companion to the canonical [Foundational Decisions](../architecture/00-foundational-decisions.md)
> and the [Discovery Report](./DISCOVERY-REPORT.md). Every `[ASSUMPTION]` tag in those documents is
> mirrored here with an implemented default, an impact-if-wrong analysis, and an owner.

## How to read this register

- **These are decisions we made to keep moving, not confirmed requirements.** Each row is a bet.
- Where a Syrian legal / regulatory / tax rule is involved we **did not invent** the rule: we
  implemented the field/flow generically and tagged it `[REQUIRES BUSINESS CONFIRMATION]`.
- Assumptions that are also **choices with live trade-offs** are cross-referenced to a matching
  `OQ-###` in [OPEN-QUESTIONS.md](./OPEN-QUESTIONS.md).
- **Confidence:** High = safe default, cheap to change · Medium = defensible, some rework if wrong ·
  Low = placeholder standing in for a business/legal answer we do not have.
- **Status:** Open · Confirmed · Rejected · Superseded.

## Legend

| Field | Meaning |
|---|---|
| ID | Stable identifier `ASM-###`; never reused. |
| Area | Domain the assumption touches. |
| Current default we implemented | What the code/UX does today. |
| Impact if wrong | Concrete rework / risk if the business confirms otherwise. |
| Confidence | High / Medium / Low. |
| Owner | Role who must confirm or overturn. |
| Status | Open / Confirmed / Rejected / Superseded. |

---

## 1. Localization, numerals & dates

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-001 | Western Arabic digits (0–9) are the correct default for Syrian business/procurement documents. | Localization / Numerals | `numeralSystem = 'latn'` default; Eastern Arabic (٠–٩) available via a per-user + per-tenant setting; all numeric formatting routed through one i18n helper. | Low-risk visually but pervasive: if Eastern Arabic is mandated by default we flip one config flag, but printed RFQ/award PDFs, exports, and tabular figures must be re-QA'd for alignment/tabular-figure rendering. | Medium | Product Owner / MOT | Open |
| ASM-002 | Gregorian calendar is the default; Hijri is an optional secondary display, not required for v1. | Localization / Dates | All persisted timestamps are UTC; UI shows Gregorian locale-aware dates; Hijri is a future toggle, not wired into deadlines/SLAs. | If Hijri dates are legally required on official documents (award letters, deadlines), we must add a dual-calendar formatter and decide which calendar governs RFQ deadlines — a real business-logic change, not cosmetic. | Medium | Product Owner / MOT | Open |
| ASM-003 | Arabic is the primary UI language (RTL) and English is a full secondary (LTR), both first-class. | Localization / i18n | Default locale `ar`; every string keyed via i18next; RTL designed-in via CSS logical properties; English maintained at parity. | Low. If English is not needed we still keep it (dev/support). If a third language (e.g. French for legacy tourism docs) is required, translation pipeline and layout QA expand. | High | Product Owner | Open |
| ASM-004 | Time zone for business deadlines and SLA clocks is Syria time (Asia/Damascus). | Localization / Deadlines | RFQ submission open/close and document-expiry windows are computed and displayed in `Asia/Damascus`; storage is UTC. | If buying entities operate across zones, "closes at 17:00" becomes ambiguous and could void submissions. Would require per-RFQ or per-org time-zone selection. | Medium | Product Owner | Open |

## 2. Registration, tenancy & identity

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-010 | Suppliers can **self-register** (open registration) with email verification, then pass compliance review before becoming active. | Registration model | Public self-registration flow → `Draft → EmailVerified → ProfileInProgress → Submitted → UnderReview → Approved`. Invite path is additive, not required. | If procurement must be **invite-only**, the public signup route is disabled and onboarding starts from an invitation token; changes landing UX, funnel metrics, and anti-abuse posture. See [OQ-002](./OPEN-QUESTIONS.md). | Medium | Product Owner / Procurement Lead | Open |
| ASM-011 | The platform is **single-instance multi-buying-entity** (shared tenancy) with row-level scoping by `OrganizationId` / `SupplierId`, not physically separate tenants. | Tenancy | One database, shared schema; RBAC row-scoping enforced at API. Suppliers are global; a supplier can transact with multiple buying entities (many-to-many, per Discovery §3.2.2). | If a buying entity demands data isolation (separate DB/schema) for governance reasons, we must introduce a tenancy strategy — significant architectural change. See [OQ-003](./OPEN-QUESTIONS.md). | Medium | Principal Architect / MOT | Open |
| ASM-012 | A supplier is a single legal entity that may hold multiple representatives (users), branches, and bank accounts under one `Supplier` aggregate. | Identity / Domain | `Supplier` aggregate holds `Representative[]`, `Branch[]`, `BankAccount[]`; first registrant becomes `supplier_admin` and can delegate `supplier_user` accounts. | If franchises/groups need a parent–child supplier hierarchy, the flat Supplier model needs a grouping layer (aligns to ERPNext `supplier_group`). | Medium | Product Owner | Open |
| ASM-013 | Email is the primary account identifier and verification channel; phone is captured but not the login credential in v1. | Identity / AuthN | ASP.NET Core Identity with email as username; email verification gates onboarding; phone stored as contact data. | If Syrian users rely on phone/OTP over email deliverability, we must add SMS OTP as a first-class verification/login channel. See [OQ-011](./OPEN-QUESTIONS.md). | Medium | Product Owner | Open |
| ASM-014 | One primary representative (`supplier_admin`) per supplier is sufficient at registration; additional admins are grantable later. | Identity / RBAC | First verified user is `supplier_admin`; can promote other users. No hard cap on admins. | Low. If a "no single point of failure" policy requires ≥2 admins, we add an onboarding nudge/requirement. | High | Product Owner | Open |

## 3. Supplier legal, financial & document data

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-020 | Syrian supplier legal/registration fields (commercial registration no., tax/financial number, legal form) exist but their **exact names, formats, and validation rules are unknown**. | Legal fields `[REQUIRES BUSINESS CONFIRMATION]` | Captured **generically**: `legalName`, `legalForm` (enum, editable list), `commercialRegistrationNumber` (free string, format-agnostic), `taxIdentifier` (free string), `registrationAuthority`, `registrationDate`. No checksum/format validation invented. | High rework surface: if formats/mandatory-ness/uniqueness rules are defined, we add validators, uniqueness constraints, and possibly duplicate-supplier detection. Wrong assumptions here can block onboarding or admit invalid entities. | Low | Legal / MOT / Procurement | Open |
| ASM-021 | Suppliers may be non-Syrian; `country` is a field and not fixed to Syria. | Legal fields | `country` defaults to Syria but is selectable (mirrors ERPNext `Supplier.country`). Tax/legal fields adapt (generic) to foreign suppliers. | If foreign suppliers are disallowed or require different documentation, we constrain the flow. Low likelihood but cheap to constrain. | Medium | Procurement Lead | Open |
| ASM-022 | The set of onboarding **document types** (e.g. commercial registration, tax card, bank letter, licenses) and their **expiry behavior** is configurable reference data, not hard-coded. | Documents | `DocumentType` reference entity drives a required-documents checklist; each type flags `requiresExpiry`, `isMandatory`. Seeded with a **generic placeholder list** pending confirmation. | If specific document types are legally mandated, we update seed data + validation; if none expire, the `ExpiringSoon/Expired` lifecycle is unused for those types. Structure holds; content must be confirmed. | Low | Compliance Reviewer / MOT | Open |
| ASM-023 | Document expiry warning lead time defaults to **30 days** before expiry (`Approved → ExpiringSoon`). | Documents / SLA | Configurable `expiryWarningDays = 30`; a Hangfire job transitions approved docs to `ExpiringSoon` then `Expired` and flags the profile incomplete. | If the business wants different lead times (e.g. 60/90 days) or per-document-type windows, config change only — low impact. | High | Compliance Reviewer | Open |
| ASM-024 | Rejected or expired mandatory documents flag the supplier profile as **incomplete** but do **not** auto-suspend an already-Active supplier. | Documents / Lifecycle | Profile shows an incomplete banner + blocks new proposal submission `[ASSUMPTION]`; `Active → Suspended` remains a manual/administrative action. | If expiry must **auto-suspend** (hard compliance), we wire an automatic transition — changes supplier ability to bid mid-RFQ, a sensitive behavior. See [OQ-006](./OPEN-QUESTIONS.md). | Medium | Compliance Reviewer / MOT | Open |
| ASM-025 | Bank account details are collected during onboarding but are **not** verified against any external banking system in v1. | Financial | `BankAccount[]` captured (bank name, branch, account/IBAN as free strings); stored encrypted at rest; no external validation. | If bank verification or a specific IBAN format is required, we add validation and possibly a manual verification step. Handling of sensitive financial identifiers must be reviewed. | Medium | Compliance / Security | Open |

## 4. Currency & tax

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-030 | Default transaction currency is **SYP (Syrian Pound)**, with multi-currency proposals and a display currency. | Currency | `Currency` reference data; SYP default; proposals carry their own currency; a display-currency preference exists. **No FX conversion/rates engine** — amounts are shown in their entered currency. | If cross-currency **comparison** in evaluation is required, we need an FX rate source and conversion policy (rate date, rounding) — new subsystem. See [OQ-007](./OPEN-QUESTIONS.md). | Medium | Procurement Lead | Open |
| ASM-031 | Syrian tax treatment on proposals (VAT/other) exists but **rates and rules are unknown**. | Tax `[REQUIRES BUSINESS CONFIRMATION]` | Proposal line items support an **optional, generic tax field** (label + percentage + amount) that the supplier enters; totals compute net/tax/gross. **No tax rate hard-coded.** | If tax is mandatory, government-set, or category-dependent, we must add a tax reference table and validation, and decide whether the portal or ERP is authoritative for tax. | Low | Finance / MOT | Open |
| ASM-032 | Monetary amounts are stored as decimal with currency code (no implicit minor-unit scaling), rounded for display only. | Currency / Data | `decimal(18,4)` + ISO-ish currency code; rounding applied at presentation and on totals per a documented policy. | If SYP requires whole-unit-only or a specific rounding convention, adjust rounding/validation. Low impact if caught early; higher if after data exists. | High | Finance | Open |

## 5. RFQ, invitation & proposal workflow

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-040 | RFQ publication requires **one** internal approver by default; the approval chain is configurable per organization. | Approval hierarchy | State machine `Draft → InternalReview → Approved → Published` with a single configurable approver role (`procurement_manager`). Multi-step chains are a config extension, not built out in v1. | If multi-level or amount-threshold-based approval is mandated, we must build an approval-chain engine (sequential/parallel, delegation). See [OQ-004](./OPEN-QUESTIONS.md). | Medium | Procurement Manager | Open |
| ASM-041 | Award decisions likewise require **one** approver by default; configurable. | Approval hierarchy | `Recommended → PendingApproval → Approved → Awarded` with a single configurable award approver. | Same as ASM-040 — thresholds/committees would require a chain engine. Award is high-stakes, so wrong assumption is costly. See [OQ-004](./OPEN-QUESTIONS.md). | Medium | Procurement Manager / MOT | Open |
| ASM-042 | A supplier submits **one** proposal per RFQ (revisable while submission is open); no multiple competing variants. | Proposal / Domain | Enforced: one `Proposal` per `(SupplierId, RfqId)`; supplier revises the draft; withdraw allowed while `SubmissionOpen`. | If "alternative bids" (variant proposals) are needed, the one-per-supplier constraint and evaluation comparison change. | Medium | Procurement Lead | Open |
| ASM-043 | Late submissions are **hard-blocked** at the deadline; no grace period. | Proposal / Deadlines | At `SubmissionClosed` the submit action is disabled server-side; draft is preserved but cannot be submitted. | If a grace window or manual late-acceptance is policy, we add a controlled override (with audit). Blocking wrongly could exclude valid bidders. | Medium | Procurement Lead | Open |
| ASM-044 | Clarifications (Q&A) are **visible to the asking supplier only** by default, with an option to broadcast an answer to all invited suppliers. | Clarifications | `Clarification` thread scoped to a supplier; procurement can mark an answer "publish to all". Default is private. | If fairness rules require **all** clarifications be broadcast (common in public procurement), the default flips and private Q&A may be disallowed. See [OQ-008](./OPEN-QUESTIONS.md). | Medium | Procurement Lead / MOT | Open |
| ASM-045 | RFQ line items and requirements are authored per-RFQ; there is no mandatory catalog/offering match at authoring time. | RFQ authoring | Free authoring of `RfqItem[]` + `Requirement[]`; supplier `Offering[]` is informational, not a hard constraint on who may bid. | If bidding must be restricted to suppliers whose offerings/categories match, we add eligibility filtering to invitations and submission. | Medium | Procurement Lead | Open |

## 6. Evaluation & award

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-050 | Evaluators score **independently and blind to peers** before consolidation. | Evaluation (canonical `[ASSUMPTION]`) | `EvaluatorScore` per assignment is not visible to other evaluators until `Consolidated`; UI hides peer scores in `InProgress`. | If open/deliberative scoring is preferred, we relax visibility and change the consolidation UX. Conversely, if blindness must be auditable/enforced (integrity), we harden it. Central to evaluation credibility. See [OQ-005](./OPEN-QUESTIONS.md). | Medium | Procurement Manager / MOT | Open |
| ASM-051 | Consolidation uses a **weighted average** of evaluator scores across criteria; criteria carry weight, max, and an optional threshold. | Evaluation / Scoring | `EvaluationTemplate.Criterion[]` = (name, weight, max, threshold, scoring type); consolidated result = weighted normalized score; sub-threshold criteria flag the proposal. | If the formula differs (e.g. lowest-price-dominant, pass/fail gates, ERPNext-style custom formulas), the scoring engine changes. Structure supports it; the default formula is the bet. | Medium | Procurement Lead | Open |
| ASM-052 | Evaluation covers both **technical and commercial** dimensions within one weighted template; there is no mandatory two-envelope (technical-then-financial) separation. | Evaluation / Process | Single template mixes technical + commercial criteria; all revealed together at consolidation. | Public procurement often mandates **two-envelope** (open technical first, financial only for qualified bidders). If required, this is a significant workflow addition. See [OQ-009](./OPEN-QUESTIONS.md). | Medium | Procurement Lead / MOT | Open |
| ASM-053 | The highest consolidated score produces the **recommendation**, but the final award can be overridden with a recorded justification. | Award | System recommends top-ranked proposal; `procurement_manager` may award a different proposal with a mandatory reason (audited). | If awards must strictly follow score rank (no override), we lock it. If more override governance is needed, we add approval on override. | Medium | Procurement Manager / MOT | Open |
| ASM-054 | Tie-breaking between equal consolidated scores is a **manual decision** with recorded justification. | Award / Scoring | On tie, system surfaces the tie and requires a manual, audited choice; no automatic tie-break rule. | If a deterministic rule is mandated (e.g. lowest price, earliest submission), we encode it. Low frequency, cheap to add. | High | Procurement Lead | Open |

## 7. Ministry visibility & governance

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-060 | The Ministry of Tourism has **read-only, cross-organization** access to **aggregate/governance** metrics, not line-level commercial values by default. | Ministry visibility | `ministry_viewer` role: read-only dashboards (activity, cycle times, participation, outcomes); **commercial amounts and individual proposal prices hidden** unless explicitly granted. | If the Ministry must see commercial values (oversight/anti-corruption) or, conversely, must **not** see supplier identities, the visibility rules and dashboard change materially. This is a governance-sensitive default. See [OQ-001](./OPEN-QUESTIONS.md). | Low | MOT / Legal | Open |
| ASM-061 | The Ministry does not take actions in workflows (cannot approve/reject/award); it observes only. | Ministry / RBAC | No write permissions granted to `ministry_viewer`; governance is monitoring, not intervention. | If the Ministry needs approval/veto authority over certain awards, we add governance-gated transitions — a workflow and RBAC change. | Medium | MOT | Open |
| ASM-062 | Ministry reporting operates on the same live operational data (no separate warehouse) in v1. | Analytics | Dashboards query the operational Postgres with read-optimized queries; no separate OLAP store. | If reporting volume/complexity grows, we may need a reporting replica or warehouse. Non-blocking, deferrable. | High | Principal Architect | Open |

## 8. ERP integration boundary

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-070 | The portal is authoritative for all pre-award flows; the ERP (ERPNext) becomes system of record for **approved supplier master + Purchase Orders** only **post-integration**. | ERP boundary | Source-of-truth split per canonical §1; every ERP-syncable aggregate carries `ExternalId (string?)`, `SyncStatus`, `LastSyncedAt`, `RowVersion`. Portal runs fully without ERP. | If the ERP must own more (e.g. supplier approval itself), the boundary and sync direction change. Low likelihood — boundary is a canonical decision. | High | Principal Architect | Open |
| ASM-071 | Award write-back to ERP produces a **Purchase Order**, emitted asynchronously via the Outbox/ACL. | ERP integration | `Award` emits an integration event; adapter maps to an ERPNext PO; failure never blocks the portal award. | If the ERP write target differs (e.g. Supplier Quotation acceptance, or no PO), the adapter mapping changes. Isolated to the integration layer. | Medium | Integration Lead | Open |
| ASM-072 | ERP identifiers are opaque naming-series **strings**; the portal never stores an integer FK to ERP and tolerates ERP IDs being assigned later. | ERP integration | `ExternalId` is a nullable string; entities are created ERP-less and back-filled on sync. | Very low — directly extracted from ERPNext (Discovery §3.2.1). If ERP switches keying scheme, only the ID type/mapping changes. | High | Integration Lead | Open |
| ASM-073 | Portal supplier categories map to ERPNext `supplier_group`, and portal proposals map to `Supplier Quotation`; exact field mappings are provisional until an integration contract is agreed. | ERP integration | Provisional mapping tables drafted in `docs/integration/`; portal collects a **superset** of ERPNext supplier fields to map cleanly later. | If ERPNext customizations change field semantics, mapping tables are revised — contained in the ACL. | Medium | Integration Lead | Open |
| ASM-074 | Sync is **eventually consistent**; brief divergence between portal and ERP is acceptable and reconciled by retries. | ERP integration | Outbox + Hangfire retries + dead-letter; idempotent adapters keyed by `ExternalId`/correlation. | If a flow needs **synchronous** ERP confirmation (e.g. real-time credit check), we add a request/response path — an exception to async-by-default. | Medium | Principal Architect / Integration Lead | Open |

## 9. Platform, security & data lifecycle

| ID | Assumption | Area | Current default we implemented | Impact if wrong | Confidence | Owner | Status |
|---|---|---|---|---|---|---|---|
| ASM-080 | Local ASP.NET Core Identity (JWT access + rotating refresh) is sufficient for v1; an external IdP (Keycloak/Entra) is a later swap. | AuthN | Local Identity, MFA-ready (Identity 2FA), IdP-swappable behind an auth abstraction. | If SSO/national identity integration is required day one, we bring the IdP forward. Abstraction limits blast radius. | Medium | Security / Architect | Open |
| ASM-081 | MFA is **available but not mandatory** for all roles in v1 (recommended for back-office/admin). | Security | 2FA can be enabled; enforcement policy not switched on globally. | If MFA is mandated (likely for `system_admin`, `award.approve`), we enforce per-role policies. Cheap to enforce. | Medium | Security | Open |
| ASM-082 | Hard delete + audit is the default; soft-delete is used only where a lifecycle state demands it (e.g. supplier deactivation, RFQ cancellation). | Data lifecycle | Per canonical §9. Deactivated suppliers and cancelled RFQs retain records with terminal states; transient/reference rows hard-delete with audit. | If regulatory retention requires keeping everything (no hard delete), we shift more entities to soft-delete + retention policy. Data-model impact. See [OQ-010](./OPEN-QUESTIONS.md). | Medium | Legal / Security | Open |
| ASM-083 | Uploaded documents are stored via the `IFileStorage` abstraction (local disk in dev, S3-compatible/MinIO in prod) with size/type limits enforced. | Files / Storage | Provider-independent storage; server-side type + size validation; virus scanning is a `[REQUIRES BUSINESS CONFIRMATION]` add-on. | If AV scanning / content inspection is mandatory before documents are visible to reviewers, we add a scan step in the upload pipeline. | Medium | Security | Open |
| ASM-084 | Notifications are **in-app + email** in v1; SMS/push are future channels. | Notifications | `Notification` aggregate + email transport; templates localized (ar/en). SMS/push behind the same abstraction, not wired. | If SMS is essential for Syrian deliverability (see ASM-013), we prioritize the SMS transport. | Medium | Product Owner | Open |
| ASM-085 | Audit log captures every state transition and permission-guarded action (actor, timestamp, from→to, reason, correlationId) and is retained indefinitely in v1. | Audit / Compliance | Per canonical §5. Append-only audit; exposed read-only to `audit.read` and Ministry governance views. | If a defined retention/rotation or tamper-evidence (hash-chain) requirement exists, we extend the audit subsystem. | High | Security / Compliance | Open |
| ASM-086 | The public identifier format `TYPE-YEAR-SEQ` (e.g. `RFQ-2026-000123`, `SUP-2026-000045`) is acceptable for user-facing references and does not need to mirror ERP naming series. | Identifiers / UX | Internal PKs are GUIDv7; public references are opaque generated codes; ERP naming-series strings live only in `ExternalId`. | If the business wants portal references to match ERP naming series exactly, we align the generator (and handle pre-ERP-assignment gaps). Low likelihood. | High | Architect | Open |

---

## Change log

| Date | Change | By |
|---|---|---|
| 2026-08-26 | Initial register seeded from canonical brief + discovery report `[ASSUMPTION]` tags. | Principal Architect |
