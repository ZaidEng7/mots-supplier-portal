# Business Requirements Document (BRD) — MOTS Supplier Portal

> **Status:** Baseline v1 · **Phase:** 0 (Discovery) · **Date:** 2026-08-26
> **Owner:** Product & Principal Architect
> Canonical alignment: [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md),
> [`DISCOVERY-REPORT.md`](./DISCOVERY-REPORT.md), [`PRODUCT-VISION.md`](./PRODUCT-VISION.md).
> Requirement IDs (**BR-###**) are stable and referenced by the functional specification.
> Unconfirmed business rules are tagged **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** and tracked in
> [`ASSUMPTIONS.md`](./ASSUMPTIONS.md) / [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md).

---

## 1. Business context & stakeholders

The Syrian tourism sector procures goods and services (supplies, furnishings, equipment, services) for
hotels and Ministry-affiliated bodies through fragmented, offline, opaque processes (see
[`PRODUCT-VISION.md` §2](./PRODUCT-VISION.md#2-the-problem-today)). The MOTS Supplier Portal digitizes
the sourcing chain — supplier onboarding → RFQ → proposal → evaluation → award — as one Arabic-first,
auditable workflow. It is standalone and independently deployable now, and integration-ready for the
existing **ERPNext** ERP (system of record for approved supplier masters and purchase orders) later.

### Stakeholders

| Stakeholder | Interest | Persona key | Surface |
|---|---|---|---|
| Suppliers (vendors) | Onboard once, win business, respond efficiently | `supplier_admin`, `supplier_user` | Supplier app (mobile + desktop) |
| Buying-entity procurement | Source competitively, compare fairly, award defensibly | `procurement_officer`, `procurement_manager` | Back-office (desktop) |
| Evaluation committees | Score consistently and independently | `evaluator` | Back-office (desktop/tablet) |
| Onboarding / compliance | Verify suppliers & documents | `onboarding_reviewer` | Back-office (desktop) |
| Ministry of Tourism | Ecosystem governance & transparency | `ministry_viewer` | Governance (read-only) |
| Platform operations | Users, roles, reference data, configuration | `system_admin` | Admin |
| Product & Architecture | Deliver premium, correct, integration-ready product | — | — |
| ERP owners (future) | Consistent supplier masters & PO write-back | — | ERPNext (external) |

## 2. Business objectives

| ID | Objective | Linked vision outcome |
|---|---|---|
| **BO-1** | Establish a single verified supplier registry so suppliers onboard once and are reusable across buying entities | O-1 |
| **BO-2** | Make procurement competition structured and comparable via defined RFQ scope and line-item proposals | O-2 |
| **BO-3** | Make awards merit-based and defensible through weighted, independent, consolidated evaluation with full audit | O-3 |
| **BO-4** | Reduce procurement cycle time with tracked timelines, deadline enforcement, and centralized clarifications | O-4 |
| **BO-5** | Give the Ministry of Tourism read-only sector visibility for governance and transparency | O-5 |
| **BO-6** | Maximize trust and inclusion through an Arabic-first, RTL, responsive, accessible, premium experience | O-6 |
| **BO-7** | Keep the portal standalone-resilient while integration-ready for ERPNext (masters back-fill, PO write-back) | O-7 |
| **BO-8** | Enforce least-privilege access and complete auditability across all sensitive actions | O-3, O-5 |

## 3. Scope

### In scope

- Supplier self-registration, email verification, and multi-step onboarding profile.
- Supplier document lifecycle (upload → review → approve/reject, expiry tracking).
- Supplier representatives/users, branches, addresses, bank accounts, categories, and offerings.
- RFQ authoring, internal review/approval, publication, invitations, and clarifications (Q&A).
- Proposal creation, draft-save, revision, submission, and withdrawal (within rules).
- Configurable weighted evaluation templates; multi-evaluator scoring; consolidation; shortlisting.
- Award recommendation, approval, and decision; emit award event for future ERP PO write-back.
- Persona dashboards (Supplier, Procurement, Management) and Ministry governance monitoring.
- Notifications, audit trail, RBAC, reference-data administration, and system configuration.
- File storage abstraction (local dev / S3-compatible prod).
- Bilingual UX (Arabic default / English), RTL/LTR, responsive, accessible (WCAG 2.2 AA).

### Out of scope (v1)

- ERP replacement: general ledger, invoicing, payments, inventory, receiving.
- Approved supplier master ownership and purchase-order lifecycle (ERPNext owns these).
- Reverse auctions / live real-time bidding marketplace.
- Contract e-signature, logistics, and supplier payment processing.
- Public unauthenticated marketplace browsing.
- Invented Syrian legal/registration/tax rule engines (fields captured generically, flagged).
- Synchronous hard dependency on ERP availability.

## 4. Capability map

Capabilities across the domain aggregates (see
[`00-foundational-decisions.md` §4](../architecture/00-foundational-decisions.md#4-core-domain--aggregates--boundaries-see-docsarchitecturedomain-modelmd)).

| Domain area | Capabilities |
|---|---|
| **Supplier** | Self-registration; email verification; multi-step profile (legal info, addresses, contacts, representatives, branches, bank accounts, categories, offerings); document upload & lifecycle; onboarding state machine; post-approval lifecycle (Active/Suspended/Deactivated); `ExternalId` mapping |
| **RFQ** | Authoring (items, requirements, attachments, timeline); internal review/approval; publication; supplier invitations; clarifications (Q&A); evaluation-template reference; state machine; cancellation with reason |
| **Proposal** | Draft-safe creation; line-item pricing; commercial terms & validity; technical response; document attachments; submission guardrails; revision on clarification; supplier-initiated withdrawal; one proposal per supplier per RFQ |
| **Evaluation** | Configurable weighted criteria templates (weight/max/threshold/scoring type); evaluator assignment; independent scoring; consolidation; comparison tooling; shortlisting |
| **Award** | Recommendation; approval workflow; award decision; award event emission for ERP PO; award/decline notifications |
| **Dashboards** | Supplier dashboard; procurement dashboard; management dashboard; Ministry read-only governance dashboards (participation, outcomes, cycle times) |
| **Admin** | User & role management; permission/RBAC; reference data (categories, document types, currencies, units, incoterms, regions); evaluation-template management; system configuration |
| **Integration** | Anti-Corruption Layer; transactional Outbox; async adapters; `ExternalId`/`SyncStatus` tracking; supplier master back-fill; award → ERP PO write-back |
| **Cross-cutting** | AuthN (Identity + JWT + refresh, MFA-ready); AuthZ (policy + permission claims, row-scoping); notifications; audit log; i18n/RTL; file storage; observability |

## 5. High-level business requirements

Priority uses **MoSCoW** (M=Must, S=Should, C=Could, W=Won't-now). Source key: **DR**=Discovery Report,
**FD**=Foundational Decisions, **VIS**=Product Vision, **ERP**=ERPNext integration surface,
**ASSUMP**=assumption pending confirmation.

### 5.1 Supplier onboarding & registry

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-001 | Suppliers can self-register with email and verify via emailed link/code | M | Supply-side adoption starts here | DR, FD §5 |
| BR-002 | Onboarding is a resumable, draft-safe multi-step profile (legal info, addresses, contacts, representatives, branches, bank accounts, categories, offerings) | M | Collect a superset of ERP supplier master, without data loss | ERP §3.1, VIS |
| BR-003 | Legal/registration/tax fields are captured generically with no invented Syrian rules | M | Do not assert unverified regulatory logic | FD §8, DR §5 |
| BR-004 | Suppliers upload required documents by configurable document type | M | Verified, reusable profile | DR §4 |
| BR-005 | Documents follow a lifecycle: Required → Uploaded → UnderReview → Approved/Rejected(reason), plus Approved → ExpiringSoon → Expired | M | Keep supplier master healthy; enforce validity | FD §5 |
| BR-006 | Rejected or expired required documents flag the profile as incomplete | M | Trust and compliance | FD §5 |
| BR-007 | Onboarding reviewers can approve, reject, or request info; supplier can resubmit | M | Governed verification loop | FD §5 |
| BR-008 | Supplier onboarding follows the canonical state machine (Draft → … → Approved/Rejected; Active ↔ Suspended → Deactivated) | M | Consistent, guarded lifecycle | FD §5 |
| BR-009 | A supplier can relate to multiple buying entities (many-to-many capable) | M | ERPNext multi-company suppliers | ERP §3.2 |
| BR-010 | Supplier Admin can invite/manage delegated Supplier Users within their supplier | S | Delegation without sharing credentials | FD §3 |
| BR-011 | Whether registration is open self-service vs. invite-only is configurable | S | Business model still open | DR §5 [ASSUMPTION] |
| BR-012 | Each ERP-syncable supplier carries a nullable string `ExternalId`, `SyncStatus`, `LastSyncedAt` | M | Stable ERP mapping without integer FKs | FD §1, §4 |

### 5.2 RFQ & sourcing

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-020 | Procurement officers author RFQs with items, requirements, attachments, and a timeline | M | Define comparable scope | DR §4, ERP §3.1 |
| BR-021 | RFQs pass internal review/approval before publication | M | Governance over what is issued | FD §5 |
| BR-022 | Approval hierarchy for RFQ publication is configurable (default single approver) | S | Hierarchy unconfirmed | DR §5 [ASSUMPTION] |
| BR-023 | RFQs follow the canonical state machine (Draft → … → Awarded → Completed; Cancellable pre-Award with reason) | M | Consistent, guarded lifecycle | FD §5 |
| BR-024 | Officers invite specific suppliers to an RFQ; invitations are tracked (sent/viewed/responded) | M | RFQ→Invitation→Proposal triad | ERP §3.1 |
| BR-025 | Suppliers and officers exchange clarifications (Q&A) tied to the RFQ | M | Centralize questions; reduce disputes | DR §4 |
| BR-026 | Submission windows enforce open/close deadlines; late submissions are blocked | M | Fair, time-bound competition | VIS, FD §5 |
| BR-027 | Each RFQ references an evaluation template | M | Fair scoring defined up front | FD §4 |
| BR-028 | RFQs and other public entities use opaque human-readable codes (e.g. `RFQ-2026-000123`), never internal PKs | M | Security; no ID enumeration | FD §4 |

### 5.3 Proposals

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-030 | Invited suppliers create proposals with line-item pricing against RFQ items | M | Structured, comparable bids | ERP §3.1 |
| BR-031 | Proposals support commercial terms, validity period, technical response, and document attachments | M | Complete, evaluable offers | FD §4 |
| BR-032 | Proposals are draft-safe and can be saved before submission | M | No lost work; deliberate submit | VIS |
| BR-033 | Exactly one proposal per supplier per RFQ | M | Integrity of competition | FD §4 |
| BR-034 | Proposals follow the canonical state machine (Draft → Submitted → … → Awarded/Declined; Withdrawn while open) | M | Guarded lifecycle | FD §5 |
| BR-035 | Suppliers can revise a proposal when a clarification/revision is requested | S | Iterative refinement, governed | FD §5 |
| BR-036 | Multi-currency proposals supported with a display currency; default SYP | S | Real-world currency variety | FD §8 |
| BR-037 | Suppliers can withdraw a proposal while submission is open | S | Supplier autonomy within rules | FD §5 |

### 5.4 Evaluation

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-040 | Evaluation templates define weighted criteria (name, weight, max, threshold, scoring type) | M | Configurable weighted model | ERP §3.1, FD §4 |
| BR-041 | Evaluators are assigned to an RFQ's evaluation | M | Committee-based scoring | FD §4 |
| BR-042 | Evaluators score independently (blind to peers) before consolidation | M | Fairness/defensibility | FD §5 [ASSUMPTION] |
| BR-043 | Scores consolidate into a comparable result across proposals | M | Merit-based comparison | FD §4 |
| BR-044 | Evaluation provides side-by-side proposal comparison tooling | S | Efficient, transparent review | DR §4 |
| BR-045 | Evaluation follows the canonical state machine (NotStarted → … → Finalized) | M | Guarded lifecycle | FD §5 |
| BR-046 | Criteria thresholds can disqualify non-compliant proposals | S | Enforce minimum standards | ERP §3.1 |

### 5.5 Award

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-050 | Procurement produces an award recommendation from consolidated results | M | Merit-based decision | FD §4 |
| BR-051 | Awards require approval before finalization (configurable approver) | M | Governance | FD §5 [ASSUMPTION] |
| BR-052 | Award decisions are recorded with rationale and full audit | M | Defensibility | BO-3, FD §5 |
| BR-053 | On award, the portal emits an award event for future ERP Purchase Order write-back | S | ERP is PO system of record | ERP §3.2, FD §1 |
| BR-054 | Awarded and unsuccessful suppliers are notified of the outcome | M | Transparency and closure | VIS |
| BR-055 | Award follows the canonical state machine (Recommended → PendingApproval → Approved/Rejected → Awarded) | M | Guarded lifecycle | FD §5 |

### 5.6 Dashboards & governance

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-060 | Suppliers have a dashboard (profile status, document validity, invitations, proposals, outcomes) | M | Supplier engagement | VIS |
| BR-061 | Procurement has a dashboard (active RFQs, submissions, evaluation progress, awards) | M | Operational control | VIS |
| BR-062 | Management has a dashboard (cycle times, throughput, participation) | S | Oversight | VIS |
| BR-063 | Ministry has read-only cross-organization governance dashboards | M | Sector transparency | FD §6, DR §4 |
| BR-064 | Whether the Ministry sees commercial values or only aggregate/anonymized metrics is configurable | M | Confidentiality unconfirmed | DR §5 [ASSUMPTION] |

### 5.7 Administration, security & cross-cutting

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-070 | RBAC with `resource.action` permissions grouped into admin-editable roles | M | Least privilege | FD §6 |
| BR-071 | Row-scoping: suppliers see own data; procurement/evaluators scoped to org; Ministry read-only cross-org; admin global | M | Data isolation | FD §6 |
| BR-072 | Authorization enforced at the API; UI checks are for affordance-hiding only | M | Never trust the client | FD §6 |
| BR-073 | Authentication via local identity with JWT access + rotating refresh tokens; MFA-ready; IdP-swappable | M | Secure, future-proof auth | FD §2 |
| BR-074 | All state changes recorded in an audit log (actor, timestamp, from→to, reason, correlationId) | M | Auditability | FD §5, §6 |
| BR-075 | Illegal state transitions are rejected by the domain, not just the UI | M | Correctness/integrity | FD §5 |
| BR-076 | Notifications for key events (invitations, deadlines, decisions, document expiry) | M | Timely action | VIS |
| BR-077 | Admin manages reference data: categories (tree), document types, currencies, units, incoterms, regions | M | Configurable domain | FD §4 |
| BR-078 | File storage via provider abstraction (local dev / S3-compatible prod) | M | Storage independence | FD §2 |
| BR-079 | Arabic-first (default `ar`, RTL) with English (`en`, LTR); every string localized | M | Arabic-first mandate | FD §8 |
| BR-080 | Numeral system defaults to Western Arabic digits, configurable to Eastern Arabic | S | Locale preference unconfirmed | FD §7 [ASSUMPTION] |
| BR-081 | Meets NFR targets: 99.5% availability, API p95 <300ms read/<800ms write, LCP <2.5s, INP <200ms, WCAG 2.2 AA, OWASP ASVS L2 | M | Enterprise quality bar | FD §9 |

### 5.8 ERP integration

| ID | Requirement | Priority | Rationale | Source |
|---|---|---|---|---|
| BR-090 | Portal operates fully with the ERP unavailable; core flows never block on ERP | M | Standalone resilience | FD §1 |
| BR-091 | Integration is async via Anti-Corruption Layer + transactional Outbox + adapters | M | Decoupled, durable integration | FD §1, §2 |
| BR-092 | ERP-syncable entities carry `ExternalId (string?)`, `SyncStatus`, `LastSyncedAt`, `RowVersion` | M | Stable mapping & concurrency | FD §4 |
| BR-093 | Approved supplier masters can back-fill from the ERP without disrupting the portal | S | ERP is master system of record | FD §1 |
| BR-094 | Award events translate to ERP Purchase Orders via the integration layer | S | ERP owns PO lifecycle | ERP §3.2 |
| BR-095 | The portal does not reuse the ERPNext stack, schema, patterns, or its Frappe web-form portal | M | Independent, premium build | FD §1, DR §3 |

## 6. Success metrics

North-star and supporting metrics are defined in
[`PRODUCT-VISION.md` §7](./PRODUCT-VISION.md#7-what-success-looks-like). Summary of what each objective is
measured by:

| Objective | Primary measure |
|---|---|
| BO-1 | Verified suppliers onboarded; median onboarding time |
| BO-2 | Avg. compliant proposals per RFQ; invitation→proposal conversion |
| BO-3 | Awards with complete recommendation + approval trail; evaluations fully scored |
| BO-4 | Median RFQ cycle time (Published → Awarded) |
| BO-5 | Ministry dashboard active usage |
| BO-6 | Arabic-first adoption; accessibility conformance; mobile usage share |
| BO-7 | Portal availability (ERP-independent); successful async sync rate |
| BO-8 | Coverage of sensitive actions by audit log; RBAC violations = 0 |

North-star: **rate of RFQs reaching a fully digital, audited award.** Numeric targets are
**[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]**, baselined from the first live quarter.

## 7. Constraints & dependencies

| Type | Item |
|---|---|
| **Architectural constraint** | Independent stack from ERP (.NET 10 / React 19 / PostgreSQL 17 vs ERPNext Python/MariaDB); no reuse of ERP code, schema, or UI |
| **Operational constraint** | Independently deployable; must function without ERP availability; 99.5% portal availability target |
| **ERP boundary** | ERPNext is system of record for approved supplier master & purchase orders; identifiers are naming-series **strings** → nullable `ExternalId (string)`, never integer FK; integration async-only via ACL + Outbox + adapters |
| **Localization constraint** | Arabic-first, RTL/LTR; SYP default currency; no invented Syrian legal/tax rules |
| **Quality constraint** | WCAG 2.2 AA; OWASP ASVS L2; premium bespoke UX (not a template component library) |
| **Security constraint** | RBAC least-privilege; row-scoping; audit on all state changes; opaque public identifiers |
| **Dependency** | Business confirmation of open questions (registration model, tenancy, document types/expiry, Ministry data visibility, approval hierarchy, numeral preference) — see [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md) |
| **Dependency** | ERPNext integration contracts (supplier master back-fill, award → PO) for Phase-N integration — see [`docs/integration/`](../integration/) |
| **Dependency** | Reference-data seeding (categories, document types, currencies, units, incoterms, regions) |

## 8. Assumptions

All assumptions above (tagged `[ASSUMPTION]`) are consolidated with proposed defaults in
[`ASSUMPTIONS.md`](./ASSUMPTIONS.md) and their resolution tracked in
[`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md). Key open items impacting business requirements:

- Supplier registration model (open self-service vs. invite-only) and tenancy (single vs. multi buying-entity). → BR-011, BR-009
- Syrian legal/registration/tax field requirements. → BR-003
- Evaluators score independently/blind before consolidation (assumed **yes**). → BR-042
- Approval hierarchy for RFQ publication and award (assumed single, configurable). → BR-022, BR-051
- Whether the Ministry sees commercial values or only aggregate/anonymized metrics. → BR-064
- Document types, expiry rules, currency/tax defaults, and numeral system preference. → BR-005, BR-036, BR-080

---

**Traceability:** BR-### identifiers are stable and consumed by the functional specification
([`docs/product/FUNCTIONAL-SPEC.md`](./FUNCTIONAL-SPEC.md)) and the backlog
([`docs/backlog/`](../backlog/)). State machines and domain terms are authoritative in
[`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md).
