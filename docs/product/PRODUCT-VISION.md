# Product Vision — MOTS Supplier Portal

> **Status:** Baseline v1 · **Phase:** 0 (Discovery) · **Date:** 2026-08-26
> **Owner:** Product & Principal Architect
> Consistent with [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) and the
> [`DISCOVERY-REPORT.md`](./DISCOVERY-REPORT.md). Unconfirmed business rules are tagged
> **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** and mirrored in [`ASSUMPTIONS.md`](./ASSUMPTIONS.md).

---

## 1. Vision statement

**Give the Syrian tourism sector a single, trustworthy, Arabic-first place where suppliers and buying
entities meet, compete fairly, and are chosen on merit — end to end, online, and auditable.**

The MOTS Supplier Portal replaces fragmented email/paper/phone procurement with a premium digital
workflow: suppliers self-onboard and maintain verified profiles once; buying entities (hotels and
Ministry-affiliated bodies) publish RFQs, invite qualified suppliers, and collect structured
proposals; evaluation committees score against configurable weighted criteria independently and then
consolidate; awards are approved and recorded with a full audit trail; and the Ministry of Tourism
gains ecosystem visibility it has never had. It runs **standalone today** and is **built to integrate
with ERPNext tomorrow** as the system of record for approved supplier master data and purchase orders.

## 2. The problem today

Procurement in the Syrian tourism sector is **fragmented, offline, and opaque**. Concretely:

| Pain | Who feels it | What it costs today |
|---|---|---|
| Suppliers re-send the same company documents to every hotel, by email or in person | Supplier Admin/User | Duplicate effort, stale/expired documents circulating, no single verified profile |
| RFQs are distributed ad hoc (email, WhatsApp, phone, paper) | Procurement Officer | Missed invitations, inconsistent scope, no reliable deadline enforcement |
| Proposals arrive in incomparable formats (PDF, images, verbal quotes) | Procurement / Evaluators | Manual re-keying, apples-to-oranges comparison, disputes over what was offered |
| Evaluation is informal and undocumented | Evaluation Committee | Perceived bias, no reproducible scoring, weak defensibility of decisions |
| Award rationale lives in someone's inbox | Procurement Manager | No traceability, hard to audit, disputes hard to resolve |
| No sector-wide visibility | Ministry of Tourism | Cannot see supplier participation, competition levels, or spend patterns |
| Nothing is Arabic-first or mobile-friendly | Everyone | Poor adoption, exclusion of smaller suppliers who work from a phone |

The net effect: **slow cycles, low trust, and decisions that cannot be defended after the fact.** No
system today owns the registration → RFQ → proposal → evaluation → award chain as one auditable flow.

## 3. Target outcomes

The portal exists to move each stakeholder from the "today" state to a measurably better one.

| # | Outcome | From → To |
|---|---|---|
| O-1 | **Onboard once, reuse everywhere** | Re-sending documents per buyer → one verified, reviewer-approved supplier profile with document lifecycle |
| O-2 | **Structured, comparable competition** | Free-form quotes → line-item proposals against a defined RFQ scope |
| O-3 | **Defensible, merit-based awards** | Informal judgement → weighted, multi-evaluator, independently-scored, consolidated decisions with audit |
| O-4 | **Faster procurement cycles** | Weeks of back-and-forth → tracked timelines with deadline enforcement and clarifications in one place |
| O-5 | **Sector transparency & governance** | No visibility → Ministry read-only dashboards over participation and outcomes |
| O-6 | **Trust and inclusion** | Desktop/email-only, Arabic as an afterthought → Arabic-first, RTL, mobile-capable, accessible |
| O-7 | **Integration-ready, not integration-blocked** | Point solutions → clean ERP boundary that back-fills approved masters and PO write-back when ready |

## 4. Who it serves

Personas are canonical in [`00-foundational-decisions.md` §3](../architecture/00-foundational-decisions.md#3-canonical-personas-see-docsproductpersonasmd-for-full)
and detailed in [`PERSONAS.md`](./PERSONAS.md).

- **Suppliers** — `supplier_admin` (primary representative) and `supplier_user` (delegated). Mobile +
  desktop. Register, onboard, keep documents valid, respond to invitations, submit and revise proposals.
- **Buying entities** (hotels / MOT-affiliated organizations) — `procurement_officer` authors RFQs and
  runs sourcing; `procurement_manager` approves publication and awards; `evaluator` scores proposals.
  Desktop/tablet back-office.
- **Onboarding / Compliance** — `onboarding_reviewer` verifies supplier profiles and documents.
- **Ministry of Tourism** — `ministry_viewer`, **read-only** cross-organization governance visibility.
- **Platform** — `system_admin` manages users, roles, reference data, and configuration.

## 5. Product principles

1. **Arabic-first, RTL-native.** Arabic is the default (`ar`, RTL); English (`en`, LTR) is secondary.
   RTL is designed in via CSS logical properties — never bolted on. Directional icons mirror.
2. **Premium, not template.** Bespoke design system (Tailwind v4 + Radix primitives), evergreen-teal
   brand with warm-stone neutrals and restrained gold. Never reads like MUI/AntD/Bootstrap.
3. **Fair by construction.** Weighted criteria, independent-then-consolidated scoring
   ([ASSUMPTION] evaluators score blind), and deadline enforcement make fairness structural, not aspirational.
4. **Auditable by default.** Every state change is recorded in the audit log (actor, timestamp,
   from→to, reason, correlationId). Illegal transitions are rejected by the domain, not just the UI.
5. **Standalone-resilient.** The portal never blocks a core flow on ERP availability; ERP is async,
   behind an Anti-Corruption Layer + transactional Outbox.
6. **Least privilege.** RBAC with `resource.action` permissions and row-scoping by Supplier/Org;
   Ministry is read-only aggregate; suppliers see only their own data.
7. **Accessible & responsive.** WCAG 2.2 AA; works from a mid-range phone to a desktop back-office.
8. **Draft-safe.** Suppliers and officers can save work in progress without fear of loss or premature submission.
9. **Configurable, not hard-coded.** Evaluation templates, document types, categories, and approval
   steps are data, not code — because Syrian regulatory specifics are still to be confirmed.
10. **Traceable decisions.** From RFQ scope to proposal line to evaluator score to award, the chain
    is linkable and explainable after the fact.

## 6. North star

> **A supplier who onboards once should be able to discover, compete for, and win business from any
> participating buying entity — entirely online, in Arabic, on a phone if they choose — and every party
> can trust the outcome because the entire journey from invitation to award is structured, fair, and
> auditable.**

## 7. What success looks like

### North-star metric

**Rate of RFQs that reach a fully digital, audited award** — i.e. RFQs where invitation, proposal
submission, evaluation, and award approval all happen in the portal with a complete audit trail, as a
percentage of all RFQs run by participating entities.

- **Definition:** `Digitally-awarded RFQs / Total RFQs published` in a period.
- **Why this one:** it is the single measure that only moves when *every* part of the value chain is
  actually adopted end to end — onboarding, sourcing, structured proposals, evaluation, and governed award.

### Supporting metrics

| Metric | What it proves | Direction |
|---|---|---|
| Verified suppliers onboarded (approved profiles) | Supply-side adoption | ↑ |
| Median onboarding time (Submitted → Approved) | Reviewer efficiency, low friction | ↓ |
| Avg. compliant proposals per RFQ | Real competition per sourcing event | ↑ |
| Invitation → proposal conversion rate | Supplier engagement & relevance of invites | ↑ |
| Median RFQ cycle time (Published → Awarded) | Speed of procurement | ↓ |
| Evaluations completed with all assigned scores | Process integrity | ↑ |
| Awards with complete recommendation + approval trail | Defensibility / governance | ↑ (target ~100%) |
| Document validity rate (non-expired required docs) | Health of supplier master | ↑ |
| Ministry dashboard active usage | Governance value delivered | ↑ |
| Portal availability (ERP-independent) | Reliability commitment (99.5% target) | ≥ target |
| Repeat participation (suppliers responding to ≥2 RFQs) | Stickiness / trust | ↑ |

Specific numeric targets are set with the business — baselines are established from the first quarter of
live usage. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** target thresholds.

## 8. Explicit non-goals

The portal deliberately does **not**:

- **Replace ERPNext.** It does not own approved supplier master data, purchase orders, financial
  postings, or tax/accounting reference data — that is the ERP's role post-integration.
- **Become an ERP.** No general ledger, invoicing, payments, inventory, or receiving.
- **Fork or extend the ERPNext stack.** No reuse of Frappe/Python/MariaDB patterns or its rudimentary
  Frappe web-form supplier portal; this is a premium replacement/complement, not a fork.
- **Run reverse auctions or a live bidding marketplace** in v1. Sourcing is RFQ → proposal → evaluation
  → award, not real-time price bidding.
- **Invent Syrian legal, registration, or tax rules.** Such fields are captured generically and flagged
  for business confirmation; the portal does not assert regulatory logic it cannot verify.
- **Handle logistics, contract e-signature, or supplier payments** in v1 (candidate future scope).
- **Provide public, unauthenticated marketplace browsing.** Access is authenticated and role-scoped.
- **Support real-time synchronous ERP dependency.** Integration is async-by-default; the portal must
  operate fully with the ERP offline.

## 9. Why now, and why standalone-then-ERP-integrated

**Why now.** The sector's procurement is manual, opaque, and hard to trust; there is a clear appetite
for a modern, Arabic-first digital process, and the technology to deliver a premium experience
(.NET 10, React 19, PostgreSQL 17) is mature and available at build time. The ERPNext ERP already
exists as a system of record but exposes only a rudimentary Frappe web-form supplier portal — leaving
the entire premium supplier-facing experience, RFQ workflow, structured proposals, evaluation, and
governance visibility unserved. That gap is the opportunity.

**Why standalone first.** Coupling the portal's launch to a full ERP integration would make delivery
slower, riskier, and dependent on ERP availability and change cycles. By building **standalone and
independently deployable**, the portal can ship value — onboarding, sourcing, evaluation, awards —
immediately, and remain fully operational even when the ERP is unavailable. The ERP owns different
concerns (approved master data, purchase orders) and is best consumed asynchronously.

**Why ERP-integrated eventually.** The ERP is the long-term system of record for approved supplier
master data and purchase orders. Rather than duplicate that authority, the portal is designed from day
one with a clean boundary: every ERP-syncable entity carries a nullable string **`ExternalId`**
(matching ERPNext's naming-series keys), and integration flows through an Anti-Corruption Layer +
transactional Outbox + adapters. When integration is switched on, approved suppliers back-fill from the
ERP and award decisions emit events that the integration layer translates into ERP Purchase Orders —
**without ever making the portal block on the ERP.** Standalone-then-integrated is not a compromise; it
is the architecture that lets the portal deliver value now and compound it later.
