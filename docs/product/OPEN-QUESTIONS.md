# Open Questions — MOTS Supplier Portal

> **Status:** Baseline v1 · **Date:** 2026-08-26 · **Owner:** Product / Principal Architect
> Companion to [ASSUMPTIONS.md](./ASSUMPTIONS.md), the canonical
> [Foundational Decisions](../architecture/00-foundational-decisions.md), and the
> [Discovery Report](./DISCOVERY-REPORT.md).

## Confirmed requirements vs. open questions

**This document tracks what is NOT yet decided.** It is deliberately separate from confirmed
requirements. The following are **confirmed** (canonical, not open) and must **not** be re-litigated here:

- Independent stack (.NET 10 + React 19 + PostgreSQL 17), independently deployable, must run without ERP.
- Arabic-first, RTL/LTR, responsive, accessible (WCAG 2.2 AA), premium UX as the #1 requirement.
- ERP boundary: portal owns pre-award flows; ERPNext owns approved supplier master + POs post-integration.
- Async integration via ACL + Outbox + adapters; `ExternalId` as nullable string.
- Core aggregates and the six state machines (onboarding, document, RFQ, proposal, evaluation, award).
- RBAC as `resource.action` permissions with row-scoping; audit on all state changes.

Everything below is **open**. Each open question pairs with an **interim decision** (usually the
matching `ASM-###`) so implementation is not blocked, but the interim decision is provisional.

## Legend

| Field | Meaning |
|---|---|
| ID | Stable identifier `OQ-###`. |
| Priority | P1 (needed before its slice ships) · P2 (needed this phase) · P3 (can wait). |
| Blocking? | Does an unanswered state currently block a specific deliverable? |

---

## Governance & visibility

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-001 | What exactly may the **Ministry of Tourism** see — aggregate governance metrics only, or line-level commercial values (prices, awarded amounts) and supplier identities? | Defines the entire Ministry surface and touches confidentiality, anti-corruption oversight, and supplier trust. Getting it wrong either leaks commercial data or blinds legitimate oversight. | (a) Aggregate/anonymized only; (b) full read incl. commercial values; (c) commercial values but anonymized suppliers; (d) configurable per metric. | Aggregate/governance metrics, read-only, commercial values hidden by default ([ASM-060](./ASSUMPTIONS.md), [ASM-061](./ASSUMPTIONS.md)). | MOT / Legal | P1 | No (interim scopes Ministry dashboards; confirm before governance slice ships) |

## Registration & tenancy

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-002 | Is supplier onboarding **open self-registration** or **invite-only** (or hybrid)? | Shapes the landing/funnel UX, anti-abuse controls, and who controls supplier admission. | (a) Open self-reg + review; (b) invite-only from a buying entity; (c) hybrid (open reg, but bidding only after invitation). | Open self-registration with compliance review; invite path additive ([ASM-010](./ASSUMPTIONS.md)). | Product Owner / Procurement Lead | P1 | No (first vertical slice is registration; confirm before public launch) |
| OQ-003 | Is a **shared multi-buying-entity** instance acceptable, or do buying entities require data isolation (separate schema/DB per tenant)? | Determines the tenancy architecture; retrofitting isolation later is expensive. | (a) Shared instance + row scoping; (b) schema-per-tenant; (c) DB-per-tenant. | Shared instance, row-level scoping by Organization/Supplier ([ASM-011](./ASSUMPTIONS.md)). | Principal Architect / MOT | P1 | No (interim is implemented; a "b/c" answer forces early rearchitecture — resolve soon) |
| OQ-011 | Is **email** a reliable primary identity/verification channel for Syrian suppliers, or is **phone/SMS OTP** required? | Email deliverability may be unreliable; wrong choice blocks onboarding completion. | (a) Email only; (b) email + SMS OTP; (c) phone-primary. | Email primary; phone captured but not a login channel ([ASM-013](./ASSUMPTIONS.md)). | Product Owner | P2 | No |

## Approval & workflow

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-004 | What is the **approval hierarchy** for (a) RFQ publication and (b) award — single approver, multi-level chain, amount thresholds, or a committee? | The approval engine's complexity depends entirely on this; awards are high-stakes and audited. | (a) Single configurable approver; (b) sequential multi-level; (c) threshold-based routing; (d) committee/quorum. | Single configurable approver for both; chain support deferred ([ASM-040](./ASSUMPTIONS.md), [ASM-041](./ASSUMPTIONS.md)). | Procurement Manager / MOT | P1 | Yes (award slice cannot finalize its approval model without this) |
| OQ-005 | Must evaluators score **independently and blind** to peers before consolidation, and must that blindness be enforced/auditable? | Central to evaluation integrity and to the evaluation UI/data-visibility rules. | (a) Blind then consolidate (enforced); (b) blind by convention only; (c) open/deliberative scoring. | Independent, peer-blind until consolidation ([ASM-050](./ASSUMPTIONS.md)). | Procurement Manager / MOT | P1 | No (interim implemented; confirm before evaluation slice ships) |
| OQ-008 | Must **all clarifications** be broadcast to every invited supplier (fairness), or can Q&A be private to the asking supplier? | Public procurement fairness rules often forbid private information advantages. | (a) All Q&A broadcast; (b) private by default + optional broadcast; (c) private allowed. | Private by default with an option to publish to all ([ASM-044](./ASSUMPTIONS.md)). | Procurement Lead / MOT | P2 | No |
| OQ-009 | Is a **two-envelope** (technical opened and qualified first, financial opened only for qualified bidders) process required, or is a single mixed evaluation acceptable? | Two-envelope is a common mandatory public-procurement control; adding it later is a major workflow change. | (a) Single mixed template; (b) two-envelope sealed; (c) configurable per RFQ. | Single weighted template mixing technical + commercial ([ASM-052](./ASSUMPTIONS.md)). | Procurement Lead / MOT | P1 | Yes (evaluation domain model differs structurally between options) |

## Suppliers, documents & compliance

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-012 | What are the **exact Syrian legal/registration/tax fields** a supplier must provide (names, formats, mandatory-ness, uniqueness)? | We refuse to invent Syrian legal rules; without this, validation and duplicate-detection cannot be finalized. | (a) Provide a defined field spec; (b) confirm generic capture is acceptable for v1. | Generic capture, no invented validation, tagged `[REQUIRES BUSINESS CONFIRMATION]` ([ASM-020](./ASSUMPTIONS.md)). | Legal / MOT / Procurement | P1 | No (onboarding ships generically; confirm before compliance-critical use) |
| OQ-013 | What is the authoritative list of onboarding **document types** and their **expiry rules**? | Drives the required-documents checklist, expiry jobs, and profile-completeness logic. | (a) Provide the list + rules; (b) accept the generic seeded placeholder list for v1. | Configurable `DocumentType` reference data, generic seed ([ASM-022](./ASSUMPTIONS.md), [ASM-023](./ASSUMPTIONS.md)). | Compliance Reviewer / MOT | P1 | No |
| OQ-006 | Should an **expired mandatory document** auto-suspend an Active supplier (and block in-flight bids), or only flag the profile? | Auto-suspension mid-RFQ is a sensitive behavior affecting live bids and fairness. | (a) Flag only; (b) block new submissions; (c) auto-suspend fully. | Flag incomplete + block new proposal submission; no auto-suspend ([ASM-024](./ASSUMPTIONS.md)). | Compliance Reviewer / MOT | P2 | No |
| OQ-014 | Is **antivirus / content scanning** of uploaded documents mandatory before reviewers can open them? | Security and reviewer safety; affects the upload pipeline and file-visibility timing. | (a) Required pre-visibility; (b) async scan, quarantine on hit; (c) not required v1. | Type/size limits enforced; AV scanning tagged `[REQUIRES BUSINESS CONFIRMATION]` ([ASM-083](./ASSUMPTIONS.md)). | Security | P2 | No |

## Currency, tax & money

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-007 | For multi-currency proposals, does evaluation require **cross-currency comparison** (and therefore an FX rate source, rate date, and rounding policy)? | Without a conversion policy, comparing proposals in different currencies is undefined. | (a) No conversion (compare within currency); (b) convert at a defined rate/date; (c) restrict RFQs to one currency. | Amounts shown in entered currency; no FX engine ([ASM-030](./ASSUMPTIONS.md)). | Procurement Lead / Finance | P2 | No |
| OQ-015 | What is the correct **Syrian tax treatment** on proposals (VAT/other), and is the portal or the ERP authoritative for tax computation? | Tax correctness is legally sensitive; we will not invent rates or rules. | (a) Provide rates/rules; (b) supplier-entered generic tax for v1; (c) tax owned by ERP only. | Optional generic tax field entered by supplier; no rate hard-coded ([ASM-031](./ASSUMPTIONS.md)). | Finance / MOT | P2 | No |
| OQ-016 | Is **Eastern Arabic (٠–٩)** or Western Arabic (0–9) the expected default numeral system for official documents? | Affects every numeric surface and printed/exported documents. | (a) Western default; (b) Eastern default; (c) per-tenant/user configurable (already supported). | Western Arabic default, configurable ([ASM-001](./ASSUMPTIONS.md)). | Product Owner / MOT | P3 | No |
| OQ-017 | Are **Hijri dates** required on any official/legal documents or deadlines, and if so which calendar governs deadlines? | Determines whether a dual-calendar formatter and deadline semantics are needed. | (a) Gregorian only; (b) Hijri display secondary; (c) Hijri governs deadlines. | Gregorian default, Hijri as future optional display ([ASM-002](./ASSUMPTIONS.md)). | Product Owner / MOT | P3 | No |

## Data, security & integration

| ID | Question | Why it matters | Options | Our interim decision | Who must answer | Priority | Blocking? |
|---|---|---|---|---|---|---|---|
| OQ-010 | Are there **data-retention or deletion** obligations (regulatory retention periods, or right-to-erasure) that override our hard-delete-plus-audit default? | Determines soft-delete scope, retention jobs, and audit retention. | (a) Hard delete + audit default; (b) retain everything N years; (c) support erasure requests. | Hard delete + audit; soft-delete only where lifecycle demands ([ASM-082](./ASSUMPTIONS.md), [ASM-085](./ASSUMPTIONS.md)). | Legal / Security | P2 | No |
| OQ-018 | Is **MFA mandatory** for any roles at launch (e.g. `system_admin`, award approvers), or optional? | Security posture for high-privilege actions; cheap to enforce if decided early. | (a) Optional; (b) mandatory for back-office/admin; (c) mandatory for all. | MFA available, not globally mandatory ([ASM-081](./ASSUMPTIONS.md)). | Security | P2 | No |
| OQ-019 | Does any flow require **synchronous** ERP confirmation, breaking the async-by-default rule (e.g. real-time supplier-master lookup or credit check)? | The async-by-default contract is central; a sync exception is an architectural carve-out. | (a) Fully async (retry/reconcile); (b) specific sync endpoints; (c) hybrid. | Eventually consistent, async-by-default ([ASM-074](./ASSUMPTIONS.md)). | Principal Architect / Integration Lead | P2 | No |
| OQ-020 | What is the confirmed **award write-back target and field mapping** in ERPNext (Purchase Order vs. other), and what triggers it (award approval vs. acceptance)? | Finalizes the integration adapter contract; provisional today. | (a) PO on award approval; (b) PO on supplier acceptance; (c) no write-back v1. | PO emitted async on award ([ASM-071](./ASSUMPTIONS.md), [ASM-073](./ASSUMPTIONS.md)). | Integration Lead / ERP Owner | P2 | No |
| OQ-021 | Should supplier **eligibility to bid** be restricted by matching category/offering, or may any active supplier respond to any invitation? | Affects invitation targeting and submission guardrails. | (a) Open to any active supplier; (b) restricted to matching categories/offerings. | Open; offerings informational only ([ASM-045](./ASSUMPTIONS.md)). | Procurement Lead | P3 | No |

---

## Resolution workflow

1. Each `OQ-###` is reviewed with its named owner; the decision is recorded here (Confirmed / Rejected).
2. On resolution, the paired `ASM-###` in [ASSUMPTIONS.md](./ASSUMPTIONS.md) is updated
   (Confirmed / Rejected / Superseded) and any canonical impact is escalated to the
   [Foundational Decisions](../architecture/00-foundational-decisions.md) owner.
3. Blocking questions are cleared before their dependent vertical slice is marked "done".

## Change log

| Date | Change | By |
|---|---|---|
| 2026-08-26 | Initial open-questions set derived from discovery gaps + canonical `[ASSUMPTION]` tags. | Principal Architect |
