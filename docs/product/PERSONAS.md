# Personas — MOTS Supplier Portal

> **Status:** Baseline v1 · **Phase:** 0 (Discovery) · **Date:** 2026-08-26
> Derived from and 100% consistent with
> [`docs/architecture/00-foundational-decisions.md`](../architecture/00-foundational-decisions.md)
> (§3 Canonical personas, §6 RBAC) and [`DISCOVERY-REPORT.md`](./DISCOVERY-REPORT.md).
> Related: [`USER-JOURNEYS.md`](./USER-JOURNEYS.md) · [`BUSINESS-PROCESSES.md`](./BUSINESS-PROCESSES.md) ·
> [`../ux/DESIGN-SYSTEM.md`](../ux/DESIGN-SYSTEM.md).

## How to read this document

There are **eight canonical persona keys**. The key (e.g. `supplier_admin`) is the stable identifier
used across code, RBAC seed data, analytics, and every other doc — never rename it. Each persona is a
representative archetype, not a real person; names and biography details are illustrative to make
design decisions concrete. Permissions summaries reference the RBAC model in
[§6 of the foundational brief](../architecture/00-foundational-decisions.md#6-rbac-model)
(`resource.action` permissions, scoped by `SupplierId` / `OrganizationId` / global).

**Arabic-first note.** All personas default to Arabic (RTL). English is a secondary, per-user
preference. Where a persona is bilingual it is stated explicitly. Numerals default to Western Arabic
digits (0–9), configurable — see [§7](../architecture/00-foundational-decisions.md#7-design-system-tokens-canonical--see-docsuxdesign-systemmd).

---

## Persona index

| # | Key | Persona | Surface | Primary device | Scope |
|---|---|---|---|---|---|
| 1 | `supplier_admin` | Supplier Admin (primary representative) | Supplier app | Mobile + desktop | Own `SupplierId` |
| 2 | `supplier_user` | Supplier User (delegated representative) | Supplier app | Mobile + desktop | Own `SupplierId` (delegated) |
| 3 | `onboarding_reviewer` | Supplier Onboarding / Compliance Reviewer | Back-office | Desktop | Own `OrganizationId` |
| 4 | `procurement_officer` | Procurement Officer (buying entity) | Back-office | Desktop | Own `OrganizationId` |
| 5 | `procurement_manager` | Procurement Manager / Approver | Back-office | Desktop | Own `OrganizationId` |
| 6 | `evaluator` | Evaluation Committee Member | Back-office | Desktop / tablet | Assigned RFQs within `OrganizationId` |
| 7 | `ministry_viewer` | Ministry of Tourism Analyst / Supervisor | Governance | Desktop | Cross-org, **read-only** |
| 8 | `system_admin` | System Administrator | Admin | Desktop | Global |

---

## 1. `supplier_admin` — Supplier Admin (primary representative)

> *"I registered our company. I'm accountable for our profile, our documents, and every bid we send.
> If a tender opens, I need to know today — not next week."*

**Illustrative profile.** Layla Haddad, 41 — General Manager & authorized signatory of a mid-sized
hotel-supplies and F&B distribution company in Damascus. Registered the company on the portal, owns
the commercial relationship with buying entities.

- **Goals**
  - Get the company **onboarded and Approved** with minimum friction and clear status at every step.
  - Never miss a relevant RFQ; win a fair share of awards.
  - Keep company documents (registration, licenses, tax, bank) valid and non-expired.
  - Delegate day-to-day bidding work to staff **without losing control or accountability**.
- **Responsibilities**
  - Owns the `Supplier` aggregate: legal info, addresses, contacts, branches, bank accounts, offerings.
  - Invites/manages `supplier_user` delegates and their permissions.
  - Final sign-off on proposals before submission; primary recipient of award/rejection outcomes.
- **Pain points (today, pre-portal)**
  - Tender information arrives late, by word-of-mouth or scattered email.
  - Re-submitting the same documents to every buyer; no single source of truth for company data.
  - Opaque evaluation — never knows *why* a bid lost.
  - Existing ERPNext web-form supplier portal is bare, English-leaning, and not mobile-friendly.
- **Context of use.** Runs the business from a phone between meetings; does focused proposal work on a
  laptop in the evening. Frequently on mobile data; occasionally weak connectivity.
- **Primary device.** **Mobile-first** for alerts, discovery, status; **desktop** for proposal authoring
  and document management.
- **Tech comfort.** Medium–high. Comfortable with web apps and banking apps; not a "power user."
- **Language.** Arabic-first; some business English.
- **Key tasks**
  1. Register company, verify email, complete profile, upload documents, submit for review.
  2. Respond to `InfoRequested` during onboarding; keep documents current before `ExpiringSoon`/`Expired`.
  3. Discover RFQs, read requirements, ask clarification questions.
  4. Create/review/approve and **submit proposals** before deadline; withdraw if needed while open.
  5. Manage delegates and company master data.
- **Success criteria**
  - Time-to-Approved is short and predictable; status is never ambiguous.
  - Zero missed relevant RFQs; every proposal submitted before close with a confirmation receipt.
  - Award/rejection outcomes arrive with a clear, respectful rationale.
- **Permissions summary** (scoped to own `SupplierId`)
  - Full: `supplier.profile.manage`, `supplier.document.upload`, `supplier.user.manage`,
    `proposal.create`, `proposal.edit`, `proposal.submit`, `proposal.withdraw`, `rfq.view`,
    `clarification.ask`, `notification.read`.
  - Cannot: see other suppliers' data, evaluate, award, or access back-office surfaces.
- **What a great day looks like.** A push/email alert flags a new RFQ that matches her categories. On
  her phone over coffee she reads the requirements and confirms it's worth bidding. That evening her
  `supplier_user` has already drafted the line pricing; she reviews on the laptop, tweaks two prices,
  approves, and submits with one confident click — receiving an immediate on-screen and emailed
  submission receipt. A separate banner reminds her a trade license is `ExpiringSoon`; she uploads the
  renewal in under two minutes. Nothing about the day felt like fighting the tool.

---

## 2. `supplier_user` — Supplier User (delegated representative)

> *"My manager handles the sign-off. I do the real work — pricing every line, attaching the specs,
> answering the buyer's questions — and I need the tool to keep my draft safe."*

**Illustrative profile.** Omar Kassab, 29 — Sales & tenders coordinator at the same supplier. Invited
by the `supplier_admin` as a delegate. Does the bulk of hands-on bidding.

- **Goals**
  - Turn RFQs into complete, accurate proposals quickly.
  - Never lose work; always know exactly what's outstanding before a deadline.
- **Responsibilities**
  - Drafts proposals: line pricing, commercial terms, technical responses, supporting documents.
  - Monitors clarification Q&A; prepares revisions when `ClarificationRequested`.
  - Maintains offering catalog and routine document uploads (within granted permissions).
- **Pain points**
  - Losing an unsaved draft; unclear which fields are still required.
  - Ambiguity over what he's *allowed* to do vs. what needs the admin's sign-off.
  - Deadline anxiety with no clear countdown or "ready to submit" checklist.
- **Context of use.** Long focused sessions at a desk; quick mobile checks for new questions/alerts.
- **Primary device.** **Desktop** for proposal authoring; **mobile** for monitoring.
- **Tech comfort.** Medium–high; fast on data entry, values keyboard-friendly tables.
- **Language.** Arabic-first.
- **Key tasks**
  1. Open an RFQ, build a `Draft` proposal (auto-saved), fill line items and terms.
  2. Ask/read clarifications; upload proposal documents.
  3. Hand off to `supplier_admin` for final submit, or submit directly **if granted**
     `proposal.submit`.
  4. Prepare revisions on `ClarificationRequested → Revised`.
- **Success criteria**
  - Draft safety: work is never lost; resume exactly where left off.
  - A crystal-clear completeness checklist and deadline countdown before submission.
- **Permissions summary** (scoped to own `SupplierId`, delegated subset)
  - Typical: `rfq.view`, `proposal.create`, `proposal.edit`, `clarification.ask`,
    `supplier.document.upload`, `notification.read`.
  - **`proposal.submit` is grantable but not default** — the admin decides whether delegates can
    submit or only prepare. Cannot manage users or company legal/bank master unless granted.
- **What a great day looks like.** He opens two open RFQs side by side, the app having preserved
  yesterday's drafts to the exact field. A persistent "3 items left / closes in 2 days" checklist keeps
  him oriented. He answers a buyer clarification, attaches the revised spec sheet, and marks the
  proposal "ready for review." The admin approves within the hour. No fire drills, no lost work.

---

## 3. `onboarding_reviewer` — Supplier Onboarding / Compliance Reviewer

> *"Every supplier we approve carries our credibility. I need to verify quickly, ask for exactly what's
> missing, and leave an audit trail that survives scrutiny."*

**Illustrative profile.** Rana Suleiman, 36 — Compliance officer in the procurement back-office of a
MOT-affiliated buying entity. Reviews supplier applications and documents.

- **Goals**
  - Approve legitimate suppliers fast; reject or return incomplete ones with precise reasons.
  - Maintain a clean, auditable, defensible onboarding record.
- **Responsibilities**
  - Works the onboarding queue: `Submitted → UnderReview`, drives `InfoRequested` / `Approved` /
    `Rejected`.
  - Reviews each `SupplierDocument`: `UnderReview → Approved | Rejected(reason)`; sets/validates
    document types and expiry.
  - Manages post-approval lifecycle flags where policy requires (`Active ↔ Suspended → Deactivated`).
- **Pain points**
  - Vague or incomplete applications requiring multiple back-and-forths.
  - No structured way to say *exactly* which field/document is wrong.
  - Hard to prove later *why* a decision was made.
- **Context of use.** Queue-driven desk work; batches of applications; frequent document viewing.
- **Primary device.** **Desktop** (multi-pane: application ↔ documents ↔ decision).
- **Tech comfort.** High for back-office tooling; expects keyboard shortcuts and dense, scannable views.
- **Language.** Arabic-first; reads bilingual supplier documents.
- **Key tasks**
  1. Triage the onboarding queue; open an application with all documents in one view.
  2. Verify legal/bank/tax fields against uploaded documents.
  3. Issue a **structured `InfoRequested`** (field-level, reasoned) or `Approve`/`Reject`.
  4. Review/approve/reject individual documents with reasons and expiry dates.
- **Success criteria**
  - Low rework loops; median review turnaround within SLA.
  - Every decision carries a reason and a complete `AuditLog` entry.
- **Permissions summary** (scoped to own `OrganizationId`)
  - `supplier.review`, `supplier.approve`, `supplier.reject`, `supplier.requestInfo`,
    `supplier.document.review`, `supplier.suspend` (policy-gated), `audit.read` (own scope),
    `notification.read`.
  - Cannot: author RFQs, evaluate, or award. **All decisions are permission-guarded and audited.**
- **What a great day looks like.** The queue is sorted by wait time. She opens the oldest application;
  the document panel shows each file, its type, and an extracted expiry date beside the matching legal
  field. One document is blurry — she rejects just that document with a one-line reason and issues a
  single, precise `InfoRequested` naming the two fields to fix. The supplier resubmits within the hour;
  she approves. The whole trail — who, when, what, why — is captured automatically.

---

## 4. `procurement_officer` — Procurement Officer (buying entity)

> *"I turn a need into a fair, well-run tender. My job is a clean RFQ, the right suppliers invited, and
> an evaluation the committee can actually run."*

**Illustrative profile.** Karim Fares, 33 — Procurement officer at a hotel / MOT-affiliated
organization. The primary author and operator of RFQs.

- **Goals**
  - Publish clear, complete RFQs; invite the right suppliers; run a smooth evaluation to a defensible
    recommendation.
- **Responsibilities**
  - Authors RFQs (items, requirements, attachments, timeline, evaluation template).
  - Sends invitations; manages the clarification Q&A; opens/closes submission.
  - Sets up evaluation: assigns evaluators, monitors scoring, builds the comparison, drafts the
    recommendation.
- **Pain points**
  - Rekeying similar RFQs from scratch; inconsistent criteria across tenders.
  - Chasing evaluators for scores; reconciling scores in spreadsheets.
  - Comparing wildly different proposal structures apples-to-apples.
- **Context of use.** Deep desk work across multi-step wizards and comparison grids; juggles several
  RFQs at different lifecycle stages.
- **Primary device.** **Desktop** (wide screens for comparison tables).
- **Tech comfort.** High; lives in the tool daily; wants templates, cloning, and bulk actions.
- **Language.** Arabic-first; some bilingual documents.
- **Key tasks**
  1. Create an RFQ (or clone a prior one), attach an `EvaluationTemplate`, submit for internal review.
  2. On `Approved → Published`, invite suppliers; run clarifications; manage `SubmissionOpen/Closed`.
  3. Assign evaluators; monitor evaluation progress; trigger consolidation.
  4. Build the comparison, draft the **recommendation**, route for `AwardApproval`.
- **Success criteria**
  - RFQs reused via templates/clones; consistent criteria; evaluations completed on time.
  - A comparison and recommendation that pass management approval without churn.
- **Permissions summary** (scoped to own `OrganizationId`)
  - `rfq.create`, `rfq.edit`, `rfq.submitForReview`, `rfq.publish` *(if delegated; else manager)*,
    `invitation.manage`, `clarification.answer`, `evaluation.setup`, `evaluation.assign`,
    `evaluation.consolidate`, `recommendation.create`, `proposal.view` (own-org RFQs), `audit.read`
    (own scope), `notification.read`.
  - Cannot approve the award itself unless also holding manager approval rights (**segregation of
    duties**).
- **What a great day looks like.** He clones last quarter's linens RFQ, adjusts three line items, reuses
  the standard evaluation template, and routes it for internal review before lunch. It's approved and
  published by afternoon; eight suppliers are invited in a couple of clicks. Two clarifications come in;
  he answers both, and every invited supplier sees the answers. When submissions close, evaluators are
  already assigned and scoring. The side-by-side comparison normalizes eight different proposals into
  one grid, and his recommendation writes itself from the weighted results.

---

## 5. `procurement_manager` — Procurement Manager / Approver

> *"I don't author tenders — I approve them. Give me the signal, the exceptions, and a clean decision
> point, with accountability on the record."*

**Illustrative profile.** Nadia Barakat, 47 — Head of procurement / department manager at the buying
entity. The approval authority for publishing RFQs and confirming awards.

- **Goals**
  - Approve RFQs and awards with confidence and speed; catch problems before they ship; keep a defensible
    governance record.
- **Responsibilities**
  - Reviews and approves/rejects `RFQ` at `InternalReview` and award at `AwardApproval`.
  - Owns segregation-of-duties: the person who runs the tender is not the sole person who awards it.
  - Oversees team throughput and exceptions.
- **Pain points**
  - Being asked to approve without enough context or with buried risks.
  - Bottlenecking the team when busy; no delegation/visibility of the pipeline.
  - Accountability gaps if approvals aren't clearly attributed.
- **Context of use.** Time-poor; approves in short windows; wants a decision-ready summary, not raw data.
- **Primary device.** **Desktop** primarily; occasional mobile for time-critical approvals.
- **Tech comfort.** Medium–high; values summaries, deltas, and one-click approve/reject with reason.
- **Language.** Arabic-first.
- **Key tasks**
  1. Review RFQs pending `InternalReview`; approve → enables `Published`, or reject with reason.
  2. Review award packages at `AwardApproval`: recommendation, comparison, rationale; `Approve`/`Reject`.
  3. Monitor the approvals pipeline and team KPIs.
- **Success criteria**
  - Fast, well-informed decisions; no surprises post-approval; every approval attributed and audited.
- **Permissions summary** (scoped to own `OrganizationId`)
  - `rfq.review`, `rfq.approve`, `rfq.reject`, `rfq.publish`, `award.review`, `award.approve`,
    `award.reject`, `recommendation.view`, `evaluation.viewResults`, `audit.read` (own scope),
    `notification.read`.
  - Typically does **not** author RFQs or enter scores (separation from `procurement_officer` /
    `evaluator`).
- **What a great day looks like.** Her approvals inbox shows three items, each with a one-screen summary:
  what's being bought, budget context, the recommended supplier, the runner-up, and the weighted score
  gap. Two are clean — approved with a click. The third flags an unusually thin score margin; she opens
  the comparison, sees the technical scores are close, and returns it with a short note asking for a
  clarification round. Every action is stamped with her name and reason. She's done in fifteen minutes
  and confident nothing slipped.

---

## 6. `evaluator` — Evaluation Committee Member

> *"I score what I'm assigned, on the criteria I'm given, honestly and independently. I don't want to
> see other people's scores until I've locked mine."*

**Illustrative profile.** Dr. Samir Aziz, 52 — Subject-matter expert (e.g. F&B quality / technical
compliance) seconded to an evaluation committee for specific RFQs.

- **Goals**
  - Score assigned proposals fairly against the weighted criteria, without bias or peer influence.
  - Finish scoring on time with minimal friction.
- **Responsibilities**
  - Reviews assigned proposals; enters `EvaluatorScore` per `Criterion` with justifying notes.
  - Submits scores (`EvaluatorSubmitted`), after which they lock for consolidation.
- **Pain points**
  - Vague criteria or unclear scoring scales.
  - Being nudged by others' opinions before locking in a judgment.
  - Clunky tools that make it hard to view the proposal and score side by side.
- **Context of use.** Focused evaluation sessions, often in a meeting room or on a tablet; may not be a
  daily portal user — needs a **guided, low-learning-curve** experience.
- **Primary device.** **Desktop / tablet** (proposal on one side, scoring rubric on the other).
- **Tech comfort.** Mixed — some evaluators are occasional/low-tech users; the UI must be obvious.
- **Language.** Arabic-first.
- **Key tasks**
  1. Open assignment list; read each proposal and its supporting documents.
  2. Enter scores against each weighted criterion with notes; respect thresholds/max scores.
  3. Submit scores → `EvaluatorSubmitted` (blind to peers per `[ASSUMPTION]` independent scoring).
- **Success criteria**
  - Clear criteria and scale; scores entered independently; submission is unambiguous and final.
- **Permissions summary** (scoped to **assigned** RFQs within `OrganizationId`)
  - `evaluation.viewAssigned`, `proposal.viewForEvaluation`, `evaluation.score`,
    `evaluation.submitScore`, `notification.read`.
  - **Cannot** see peers' scores before consolidation
    ([§5 `[ASSUMPTION]`](../architecture/00-foundational-decisions.md#5-canonical-state-machines-authoritative--see-docsproductbusiness-processesmd)),
    author RFQs, or award. Access limited strictly to assigned proposals.
- **What a great day looks like.** He receives an assignment notification, opens the portal, and finds
  exactly three proposals waiting — no clutter. Each opens in a split view: the proposal and its specs on
  one side, the scoring rubric with clear 0–max scales and criterion descriptions on the other. He scores
  each criterion, jots a one-line justification, and submits. The tool confirms his scores are locked and
  won't be revealed to anyone until the committee consolidates. He never once saw another evaluator's
  opinion, and the whole thing took half an hour.

---

## 7. `ministry_viewer` — Ministry of Tourism Analyst / Supervisor

> *"I don't run tenders — I watch over the ecosystem. Show me health, fairness, and trends across all
> entities, without touching a single decision."*

**Illustrative profile.** Hala Deeb, 44 — Analyst / supervisor at the Ministry of Tourism responsible
for governance and oversight of the supplier ecosystem across buying entities.

- **Goals**
  - Understand ecosystem health: participation, cycle times, award concentration, fairness signals.
  - Produce oversight reports for leadership; spot anomalies without operational involvement.
- **Responsibilities**
  - Read-only, cross-organization monitoring and governance analytics.
  - No operational actions — cannot create, edit, evaluate, or award anything.
- **Pain points**
  - No unified, trustworthy cross-entity view today; data scattered across organizations.
  - Ambiguity over what she is *permitted* to see (commercial values vs. aggregates).
- **Context of use.** Dashboard-centric; periodic deep dives; exports for reporting.
- **Primary device.** **Desktop** (governance dashboards, charts, exports).
- **Tech comfort.** Medium–high; comfortable with dashboards and filters; not a data engineer.
- **Language.** Arabic-first.
- **Key tasks**
  1. Review cross-organization dashboards: active RFQs, supplier participation, cycle times, awards.
  2. Drill into trends and anomalies (e.g. award concentration, repeated single-bid tenders).
  3. Export governance reports.
- **Success criteria**
  - A trustworthy, always-current ecosystem view; clear, permission-appropriate data boundaries.
- **Permissions summary** (**cross-organization, strictly read-only**)
  - `governance.dashboard.view`, `governance.report.export`, `rfq.viewAggregate`,
    `supplier.viewAggregate`, `award.viewAggregate`, `notification.read`.
  - **Commercial-value visibility is policy-gated** — whether the Ministry sees actual bid amounts or
    only aggregate/anonymized metrics is
    **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** (see
    [`OPEN-QUESTIONS.md`](./OPEN-QUESTIONS.md)); the portal defaults to **aggregate/anonymized** until
    confirmed. No write access of any kind, in any scope.
- **What a great day looks like.** She opens the governance dashboard and sees the whole sector at a
  glance: how many tenders are live, how many suppliers are actively participating, average time from
  publish to award, and award distribution across suppliers. A widget flags three tenders that received
  only a single bid this month — a fairness signal worth a note to leadership. She filters by region,
  exports a clean summary, and never has to ask anyone for a spreadsheet. She sees exactly what she's
  cleared to see, nothing operational, and trusts the numbers are live.

---

## 8. `system_admin` — System Administrator

> *"I keep the platform correct and safe: the right people, the right permissions, the reference data,
> and a clean audit trail. I stay out of the business decisions themselves."*

**Illustrative profile.** Tarek Nasser, 38 — Platform administrator responsible for configuration,
access management, reference data, and operational health of the portal.

- **Goals**
  - Correct access (least privilege), clean reference data, healthy integrations, and complete auditability.
- **Responsibilities**
  - Manages users, roles, and permission sets; provisions organizations and org units.
  - Maintains reference data: `Category` tree, `DocumentType`, currencies, units, incoterms, regions,
    evaluation templates catalog.
  - Configures system settings (localization defaults, numerals, notification templates) and monitors
    integration/outbox health.
- **Pain points**
  - Ad-hoc permission requests with no clear model; risk of over-privileging.
  - Stale or inconsistent reference data breaking downstream flows.
  - Blind spots in integration failures (Outbox → ERP) and background jobs.
- **Context of use.** Admin console; config-heavy, occasional but high-stakes changes; incident response.
- **Primary device.** **Desktop**.
- **Tech comfort.** Very high; expects robust admin tooling, safeguards, and observability.
- **Language.** Arabic-first; comfortable bilingual.
- **Key tasks**
  1. Create/manage users, assign roles, edit role→permission mappings (RBAC).
  2. Provision organizations/org units; manage supplier lifecycle flags at the platform level.
  3. Maintain reference data and evaluation-template catalog; configure localization/notifications.
  4. Monitor Hangfire jobs, Outbox/integration health, and read the global `AuditLog`.
- **Success criteria**
  - Least-privilege access with no orphaned/over-broad grants; reference data consistent; integrations
    observable and recoverable; full audit coverage.
- **Permissions summary** (**global**)
  - `admin.users.manage`, `admin.roles.manage`, `admin.permissions.manage`, `admin.org.manage`,
    `admin.referenceData.manage`, `admin.settings.manage`, `admin.integration.monitor`,
    `audit.read` (global), `notification.manage`.
  - Global scope, but **not** a business actor: does not author RFQs, evaluate, or award (those remain
    with procurement/evaluation roles). Sensitive actions are themselves audited.
- **What a great day looks like.** A new procurement officer joins; Tarek assigns the seeded role in
  seconds, and the person has exactly the right permissions — no more. He adds three new subcategories to
  the `Category` tree and a new document type with an expiry rule, and downstream onboarding picks them up
  immediately. The integration monitor is all green — the Outbox is draining to the ERP with zero
  dead-lettered messages. When a manager asks "who changed this RFQ's template?", the global audit log
  answers in one search. A quiet, controlled, fully observable day.

---

## Summary matrix — persona × primary surface × top jobs-to-be-done × device

| Persona (`key`) | Primary surface | Top 3 jobs-to-be-done | Primary device |
|---|---|---|---|
| **Supplier Admin** (`supplier_admin`) | Supplier app | 1. Get onboarded & Approved with clear status · 2. Never miss a relevant RFQ · 3. Review & **submit** winning proposals on time | Mobile-first + desktop |
| **Supplier User** (`supplier_user`) | Supplier app | 1. Build complete, accurate proposals fast · 2. Keep drafts safe & know what's outstanding · 3. Manage clarifications & document uploads | Desktop (author) + mobile (monitor) |
| **Onboarding Reviewer** (`onboarding_reviewer`) | Back-office | 1. Triage & verify supplier applications · 2. Issue precise `InfoRequested` / approve / reject · 3. Review documents with reasons & expiry | Desktop |
| **Procurement Officer** (`procurement_officer`) | Back-office | 1. Author/clone RFQs with reusable templates · 2. Invite suppliers & run clarifications · 3. Set up evaluation, compare, draft recommendation | Desktop (wide) |
| **Procurement Manager** (`procurement_manager`) | Back-office | 1. Approve/reject RFQs at internal review · 2. Approve/reject awards with rationale · 3. Oversee pipeline & KPIs | Desktop (+ mobile for urgent) |
| **Evaluator** (`evaluator`) | Back-office | 1. Read assigned proposals · 2. Score weighted criteria independently · 3. Submit & lock scores on time | Desktop / tablet |
| **Ministry Viewer** (`ministry_viewer`) | Governance | 1. Monitor ecosystem health cross-org · 2. Spot fairness/anomaly signals · 3. Export governance reports | Desktop |
| **System Admin** (`system_admin`) | Admin | 1. Manage users/roles/permissions (RBAC) · 2. Maintain reference data & templates · 3. Monitor integrations & audit | Desktop |

---

## Cross-persona design implications

- **Arabic-first, RTL by default** for every persona; English is per-user opt-in. Logical properties,
  mirrored directional icons, and locale-aware numerals/dates apply everywhere
  ([§7–8](../architecture/00-foundational-decisions.md#7-design-system-tokens-canonical--see-docsuxdesign-systemmd)).
- **Mobile-critical personas** are the two supplier personas (discovery, alerts, status, submit). Every
  other persona is desktop-primary; `evaluator` must also work well on **tablet**, and
  `procurement_manager` needs a **mobile approval** path.
- **Least-privilege & segregation of duties** is a first-class design constraint: `procurement_officer`
  (runs tenders) ≠ `procurement_manager` (approves) ≠ `evaluator` (scores). The UI hides affordances the
  user lacks, but the API is the source of truth
  ([§6](../architecture/00-foundational-decisions.md#6-rbac-model)).
- **Auditability** matters most to `onboarding_reviewer`, `procurement_manager`, `ministry_viewer`, and
  `system_admin` — every state change is attributed and reasoned.
- **Low-learning-curve, guided flows** are essential for the occasional users: `evaluator`, and to a
  degree `procurement_manager` and `ministry_viewer`.
- **Draft safety and deadline clarity** are make-or-break for `supplier_user` and `supplier_admin`.

> Every persona's detailed step-by-step experience is mapped in
> [`USER-JOURNEYS.md`](./USER-JOURNEYS.md).
