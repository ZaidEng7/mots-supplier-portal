# Discovery Report — MOTS Supplier Portal

> **Date:** 2026-08-26 · **Phase:** 0 (Discovery) · **Author:** Principal Architect

## 1. Objective

Establish, before any significant implementation, (a) the state of the target repository, (b) the
relevant business/domain and integration context of the existing ERP, and (c) the gaps, assumptions,
and risks that shape the product. This report is the entry point to the Phase-0 deliverable set.

## 2. Target repository state

- `/Users/issamshadid/Repos/supplier_portal` was **empty / non-existent** at discovery — this is a
  **greenfield** project. No prior code, config, or docs to preserve or migrate.
- Initialized as a fresh git repository with a professional structure (`docs/`, `src/`, `tests/`,
  `infrastructure/`, `scripts/`, `.github/`).

## 3. ERP inspection (integration/domain context only — stack deliberately NOT reused)

The ERP is **ERPNext** built on the **Frappe framework** (Python, MariaDB, Jinja/JS). We treat it as
an **external system of record and future integration dependency**, not a codebase to extend.

### 3.1 Relevant ERPNext `buying` doctypes (the integration surface)

| ERPNext doctype | Portal concept it maps to | Notes for integration |
|---|---|---|
| `Supplier` | Supplier master (approved) | Fields: `supplier_name, country, tax_id, supplier_group, supplier_type, default_currency, payment_terms`; multi-company via `companies` table; bank via `accounts`; **`portal_users`** already links web users; primary contact/address are linked doctypes. **Naming series string ID.** |
| `Request for Quotation` (+ `_item`) | RFQ | Internal doc; status `Draft/Submitted/Cancelled`; `suppliers` (invited) + `items`; `schedule_date`, `incoterm`, addresses. |
| `Request for Quotation Supplier` | RFQ Invitation | `supplier, contact, quote_status, email_sent`. |
| `Supplier Quotation` (+ `_item`) | **Proposal** | Line items, taxes, totals, `currency`, status `Draft/Submitted/Stopped/Cancelled/Expired`. |
| `Supplier Scorecard` (+ criteria/period/variable/standing) | **Evaluation framework** | **Weighted criteria** (`criteria_name, max_score, formula, weight`), periods, standings, notify flags. Confirms a configurable, weighted evaluation model is the right target. |
| `Portal User` / ERPNext `www` supplier portal | Existing rudimentary supplier portal | ERPNext already exposes a basic Frappe web-form supplier portal; **our portal is the premium replacement/complement**, not a fork. |

### 3.2 Key integration facts extracted

1. **String identifiers.** ERPNext primary keys are naming-series **strings** → portal stores a
   nullable **`ExternalId: string`** per synced entity; never an integer FK to the ERP.
2. **Multi-company suppliers.** A supplier can relate to multiple companies → our Supplier↔Organization
   model must be many-to-many-capable.
3. **Rich supplier master.** tax_id, supplier_group/type, currency, payment terms, bank accounts,
   multiple contacts/addresses — our onboarding must collect a superset and map cleanly.
4. **Weighted scorecard exists.** Validates a **configurable evaluation template** (criteria + weight +
   threshold), not hard-coded criteria.
5. **RFQ is buyer-internal; supplier responds via Quotation.** Confirms the RFQ→Invitation→Proposal
   triad; our portal adds the missing premium supplier-facing UX, clarifications, and evaluation flow.
6. **Award → Purchase Order.** The natural ERP write-back on award is a **Purchase Order**; the portal
   emits an award event that the integration layer translates to an ERP PO.

## 4. What the ERP does NOT give us (portal must own)

- Modern, premium, Arabic-first supplier onboarding & document lifecycle UX.
- RFQ authoring workflow with internal review/approval, invitations, and Q&A/clarifications.
- Structured proposal creation with draft safety, revisions, and submission guardrails.
- Multi-evaluator, independent-then-consolidated evaluation with comparison tooling.
- Persona dashboards (Supplier, Procurement, Ministry, Management) and governance monitoring.
- Notification architecture, audit trail tuned to procurement, and fine-grained RBAC.

## 5. Gaps & ambiguities identified (→ tracked)

The following require business confirmation and are recorded with proposed defaults in
[`ASSUMPTIONS.md`](ASSUMPTIONS.md) and [`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md):

- Syrian legal/registration/tax field requirements for suppliers (kept generic, no invented rules).
- Whether evaluators score independently/blind before consolidation (assumed **yes**).
- Approval hierarchy for RFQ publication and award (assumed single approver, configurable).
- Whether Ministry can see commercial values or only aggregate/anonymized metrics.
- Supplier self-registration open vs. invite-only; single vs. multi buying-entity tenancy model.
- Document types and expiry rules; currency/tax defaults; numeral system preference.

## 6. Constraints confirmed

- **Independent stack** from ERP (✅ .NET + React + PostgreSQL vs ERPNext Python/MariaDB).
- **Independently deployable**; must function without ERP availability.
- **Integration-ready** via ACL + Outbox + adapters and stable external-ID mapping.
- **UX is the #1 requirement** — premium, Arabic-first, RTL/LTR, responsive, accessible.

## 7. Recommended path (Phase 0 → Phase 1)

1. Publish the Phase-0 deliverable set (this report + product/UX/architecture/backlog docs). *(in progress)*
2. Stand up the design system + app shells (backend skeleton, frontend skeleton, CI).
3. Deliver the **first vertical slice**: Supplier registration + email verification (UI+API+DB+tests).
4. Proceed through the roadmap by vertical slices, reviewing UX/security/RTL after each.

## 8. Status of the 21-item Initial Deliverable

Tracked in [`docs/backlog/PHASE-0-DELIVERABLES.md`](../backlog/PHASE-0-DELIVERABLES.md).
