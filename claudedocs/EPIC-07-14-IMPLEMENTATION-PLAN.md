# Epics 7–14 Implementation Plan (RFQ → Award) — Buyer/Procurement Domain

> Planning document only. No code, migrations, or tests were written to produce this. Covers
> EPIC-07 (RFQ) through EPIC-14 (Award), i.e. ROADMAP Phases 4–8. Every claim below is cited to a
> real document/ID (`FR-*`, `BR-*`, `BRULE-*`, `ASM-*`, `OQ-*`, `FEAT-*`/`STORY-*`, `SCR-*`). Where
> the underlying docs disagree or a decision is genuinely open, that is called out explicitly rather
> than resolved by guessing.
>
> Sources read directly and in full for this plan: `docs/backlog/ROADMAP.md`,
> `docs/product/OPEN-QUESTIONS.md`, `docs/product/BUSINESS-PROCESSES.md` (state machines
> cross-checked directly — not taken on trust), `docs/architecture/DOMAIN-MODEL.md`,
> `docs/architecture/DATABASE-MODEL.md`. Sources extracted verbatim via research passes:
> `docs/backlog/BACKLOG.md` (EPIC-07–14), `docs/product/FUNCTIONAL-REQUIREMENTS.md` (relevant
> `FR-*`), `docs/product/BUSINESS-RULES.md` + `BUSINESS-REQUIREMENTS.md` + `ASSUMPTIONS.md`
> (relevant `BRULE-*`/`BR-*`/`ASM-*`), `docs/ux/SCREEN-INVENTORY.md` +
> `docs/ux/SCREEN-SPECIFICATIONS.md` + `docs/ux/USER-FLOWS.md` +
> `docs/product/PERSONAS.md` + `docs/product/USER-JOURNEYS.md`.

---

## Step 1 — Full requirements pass

### 1.1 What's already canonical (do not re-litigate)

From `OPEN-QUESTIONS.md`'s own "confirmed vs open" framing and `BUSINESS-PROCESSES.md`: the six
canonical state machines (including RFQ, Proposal, Evaluation, Award) are confirmed and not open;
RBAC as `resource.action` with row-scoping is confirmed; async ERP integration via Outbox + ACL is
confirmed; `ExternalId` is a nullable string, never an integer FK.

### 1.2 The three blocking open questions, named plainly (not resolved)

**OQ-004 — Approval hierarchy for RFQ publication and award.** *Blocking: Yes — "award slice
cannot finalize its approval model without this."* Options on the table: (a) single configurable
approver, (b) sequential multi-level, (c) threshold-based routing, (d) committee/quorum. **Interim
decision we build against now:** single configurable approver for both RFQ publish and award
(`ASM-040`, `ASM-041`), with the domain modeled so a multi-step chain is a config extension, not a
rewrite — `Award.Approval[]` is already an ordered array (`DATABASE-MODEL.md` §2.6: `approval`
table has `step_no int`), and `BRULE-072`/`BRULE-074` describe an amount-band authority matrix as
the eventual real shape. **This means:** EPIC-07's FEAT-07.4 and EPIC-14's FEAT-14.2 can be built
now on the single-approver interim, but the *authority-matrix/threshold-routing* logic
(`BRULE-072`, `BRULE-074`) cannot be finalized — it will need a second pass once OQ-004 resolves.
Owner: Procurement Manager / MOT. Priority P1.

**OQ-005 — Must evaluators score independently and blind to peers, and must that be
enforced/auditable?** *Blocking: No ("interim implemented; confirm before evaluation slice
ships")* — but it is structurally load-bearing for EPIC-11's entire API scoping model. Interim
decision: independent, peer-blind until consolidation (`ASM-050`), enforced at the API row-scoping
layer (each evaluator's `GET` only ever returns their own `EvaluatorScore` rows) — not by UI
convention. **This means:** EPIC-11 can and should be built now on this interim, but the *specific
enforcement mechanism* (hard API-level blindness vs. convention) should be flagged as confirmed-by-
build, i.e. verified with an explicit negative/authz test per the Phase 7 gate criteria in
`ROADMAP.md`, so a later "blind by convention only" answer would be a scoping-loosening change, not
a rearchitecture.

**OQ-009 — Two-envelope (technical-then-financial) vs single mixed evaluation.** *Blocking: Yes —
"evaluation domain model differs structurally between options."* Options: (a) single mixed
weighted template (current interim, `ASM-052`), (b) two-envelope sealed (technical opened/qualified
first, financial opened only for qualified bidders), (c) configurable per RFQ. **This is the single
biggest structural risk in this entire plan.** If the real answer is (b) or (c), it changes:
`EvaluationTemplate.Criterion.dimension` can no longer just tag `{Technical, Commercial,
Compliance, Delivery}` as informational metadata — it becomes a *sequencing gate* requiring a new
"financial envelope" concept, a state on `Evaluation` for "technical phase closed, financial
unsealed", and comparison/UI that must hide `ProposalItem` pricing entirely from evaluators until
that gate opens. **This means:** EPIC-11 and EPIC-12 (Comparison) can be built now on the
single-mixed-template interim, but should be built with the `Criterion.dimension` split already
present in the schema (it already is, per `DATABASE-MODEL.md` §2.5 `criterion_dimension`) so that a
later two-envelope answer is a phased-gate addition to `Evaluation`'s state machine rather than a
teardown of the scoring model. Do **not** build any code that assumes financial and technical
criteria are always revealed together in one screen without an eye toward this split. Owner:
Procurement Lead / MOT. Priority P1.

### 1.3 Other open questions that touch this domain (non-blocking, build now, confirm later)

- **OQ-007** (FX/cross-currency comparison) — interim: no FX engine, amounts shown in entered
  currency (`ASM-030`). Affects EPIC-12 FEAT-12.3 directly (`[ASSUMPTION]` on rate source in both
  the FR extraction and the UX extraction — `SCR-432` spec literally tags "normalize-currency
  requires an FX/display-currency choice `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`").
- **OQ-008** (must all clarifications broadcast, or can Q&A be private?) — interim: private by
  default with an option to publish to all (`ASM-044`). Affects EPIC-10 FEAT-10.2.
- **OQ-021** (bid eligibility restricted by category match, or open to any Active supplier?) —
  interim: open; offerings informational only (`ASM-045`). Affects EPIC-08 FEAT-08.2/08.6.
- Two more surfaced only in the BRULE/ASM extraction, not in `OPEN-QUESTIONS.md` itself but real
  and material — **flagging them as questions that probably belong there**:
  - Whether **commercial (price) revisions** are allowed during a proposal clarification loop, or
    clarification is technical-only with price locked (`BRULE-050`, `ASM-` not explicitly numbered
    but described in both the BUSINESS-PROCESSES §4.2 exception note and `USER-FLOWS.md` §8.2:
    "default: price-affecting fields require explicit procurement authorization").
  - **Tie-break rule** at ranking (`BRULE-069`, `ASM-054`) — interim: manual, audited decision, no
    automatic rule.

### 1.4 State machines — verified directly against `BUSINESS-PROCESSES.md` (not summarized)

**RFQ** (`BUSINESS-PROCESSES.md` §3): `Draft → InternalReview → Approved → Published →
SubmissionOpen → SubmissionClosed → UnderEvaluation → Clarification* → Shortlisting →
Recommendation → AwardApproval → Awarded → Completed`; `Cancelled` reachable from any pre-Awarded
state (reason mandatory). Full transition table (actor/permission/guard/side-effect/audit) is in
§3.1 of that document — every FR-RFQ item in §2 below maps 1:1 onto a row there.

**Proposal** (§4): `Draft → Submitted → UnderReview → (ClarificationRequested → Revised →
UnderReview)* → Shortlisted | NotSelected → AwardOffered → Awarded | Declined`; supplier-initiated
`Withdrawn` allowed only while `SubmissionOpen`.

**Evaluation** (§5): `NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated →
Finalized`; `Consolidated → InProgress` re-open path exists (reason mandatory).

**Award/Approval** (§6): `Recommended → PendingApproval → Approved | Rejected → Awarded →
ErpPoRequested → ErpPoSynced` (with `ErpPoFailed → ErpPoRequested` retry loop); `Rejected →
Recommended` rework path.

These four state machines are what EPIC-07, 09, 11, 14 each own respectively. EPIC-08
(Invitations) and EPIC-10 (Clarifications) do **not** have their own top-level state machines —
they are child entities of RFQ (`Invitation.status`, `Clarification.visibility`) with their own
small enums, not full aggregates. EPIC-12 (Comparison) and EPIC-13 (Procurement Workflow) have
**no state machine at all** — Comparison is a read-only derived view over Proposal + Evaluation,
and Procurement Workflow is an orchestration/UX layer over the other five aggregates' states, not
an aggregate of its own. This matters for the domain-model section of each epic below.

### 1.5 Domain model — the five aggregates this plan touches (`DOMAIN-MODEL.md` §5.4–5.8)

| Aggregate | Owns | ERP-synced | Public ref | Relation to already-built aggregates |
|---|---|---|---|---|
| **RFQ** | RfqItem[], Requirement[], Invitation[], Clarification[], Attachment[], Timeline(VO), EvaluationTemplateRef(VO) | Yes | `RFQ` | References `Organization` (built, EPIC-01/21) and `Supplier` (built, EPIC-02/03) by Id only — never a navigation property. |
| **Proposal** | ProposalItem[], ProposalDocument[], CommercialTerms(VO), TechnicalResponse, Validity(VO), Totals(VO, derived) | Yes | `PRO` | References RFQ and Supplier by Id; **exactly one per (SupplierId, RfqId)**, enforced by both a DB unique constraint and the domain (`BRULE-042`). |
| **EvaluationTemplate** | Criterion[] | No | `EVT` | Portal-only; referenced by RFQ via a **version-snapshotted** ref (`RFQ.EvaluationTemplateRef{EvaluationTemplateId, snapshotVersion}`) so later template edits never retroactively change a live RFQ. |
| **Evaluation** | EvaluationAssignment[], EvaluatorScore[], ConsolidatedResult | Result feeds ERP indirectly (via Award) | `EVL` | References RFQ (1:1, `evaluation.rfq_id` is `UNIQUE`) and the RFQ's snapshotted EvaluationTemplate; `EvaluatorScore` references `Proposal` by Id. |
| **Award** | Recommendation, Approval[], AwardDecision, ExternalPurchaseOrderRef(VO) | Yes (→ PO) | `AWD` | References RFQ (1:1, `award.rfq_id` is `UNIQUE`) and the winning `Proposal` by Id. |

All five follow the same aggregate-boundary rules already used by `Supplier`/`Offering` in this
codebase (`DOMAIN-MODEL.md` §8): one aggregate loaded/saved per command, cross-aggregate references
by `Id` only, invariants enforced only through root methods, `RowVersion`-style concurrency at the
root (here: Postgres `xmin`, per `DATABASE-MODEL.md` §8 — **note**: this is a different concurrency
mechanism than the explicit `RowVersion` column pattern; confirm which convention the already-built
`Supplier`/`Offering` entities actually use in code before assuming `xmin` — the docs say `xmin`,
but this should be verified against the current `AppDbContext` configuration in the first EPIC-07
slice, since getting this wrong silently breaks optimistic concurrency).

### 1.6 Database conventions to follow (`DATABASE-MODEL.md`, verified directly)

- **Schemas**: `rfq`, `proposal`, `evaluation`, `award` — four new logical schemas, following the
  existing `identity`/`supplier` pattern.
- **PKs**: `uuid` GUIDv7, generated in the app (`Guid.CreateVersion7()`), never `gen_random_uuid()`.
- **Public reference codes**: `RFQ-YYYY-NNNNNN`, `PRO-YYYY-NNNNNN`, `AWD-YYYY-NNNNNN`, allocated via
  the existing `reference.code_sequence` atomic `INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING`
  pattern (§1.2) — this table and pattern should already exist from EPIC-02/03's `SUP-YYYY-NNNNNN`
  codes; reuse it, don't reinvent it.
- **JSONB**: exactly two new JSONB columns per §6 — `rfq.evaluation_template_snapshot` (frozen
  criteria+weights at RFQ approval, read-only, unindexed) and `proposal.technical_response`
  (answers to variable per-RFQ requirements). `consolidated_result.breakdown` is a third. **Do not**
  reach for JSONB for anything that is filtered/joined/aggregated/FK'd — the rule of thumb in
  §6 applies exactly as much here as it did to `Offering.attributes`.
- **Money**: `numeric(18,4)` + `char(3)` currency code, never `float`. Totals are **always
  server-computed**, never trusted from the client (`BRULE-055`).
- **Concurrency**: Postgres `xmin` mapped as EF concurrency token — this is how two officers racing
  to publish the same RFQ, or two evaluators racing to submit, get a `409` + reload-and-retry, not a
  lost update (`DATABASE-MODEL.md` §8, and directly required by `FR-PWF-005`/`BRULE-` — see EPIC-13).
- **Soft-delete**: `rfq.rfq`, `proposal.proposal`, `award.award` are all on the **soft-delete** list
  (§9) — "procurement record of record", "bid evidence for audit/dispute", "contractual outcome;
  drives ERP PO; never truly deleted." `evaluation.evaluator_score` is explicitly **hard-delete
  pre-finalize** ("draft scores; after Finalized the consolidated result is the retained record").
  `EvaluationTemplate` and `Evaluation` itself aren't listed either way in §9 — treat as hard-delete
  default unless a later pass finds a reason otherwise (Draft RFQs/proposals are explicitly
  hard-delete-allowed per §9, consistent with "never-published working data").
- **ERP sync columns**: `external_id`, `sync_status`, `last_synced_at`, `last_sync_error` on
  `rfq.rfq`, `proposal.proposal`, `award.award` only (§1.1) — **not** on `evaluation_template` or
  `evaluation` (portal-only, per the aggregate catalogue in `DOMAIN-MODEL.md` §3).
- **Row-scoping indexes**: every scoped table needs `(organization_id, state)` or
  `(supplier_id, state)` composite indexes, per §11 — this is how RBAC row-scoping stays fast, and
  it's a hard requirement, not an optimization to defer.

---

## Step 2 — Per-epic plans

Each epic section follows: scope (FR/BR cites) → domain model → state machine → API surface →
frontend surface → dependencies → open questions/blockers → suggested build order.

---

### EPIC-07 — RFQ (authoring & lifecycle)

**Scope summary.** `FR-RFQ-001..013`, traces to `BR-020..028`. Goal (verbatim from `BACKLOG.md`):
"Enable procurement to author RFQs (items, requirements, attachments, bound evaluation template),
pass internal review/approval, publish, and drive the full RFQ state machine with state-gated
editing, opaque public references, timeline automation, and cancel-with-reason — all
permission-guarded and audited." Primary phase: **Phase 4**.

**Domain model.** Aggregate root `RFQ` (schema `rfq`, table `rfq.rfq`). Entities: `RfqItem` (line,
category, quantity, UoM, spec, `is_unit_price`, `is_optional`), `Requirement` (mandatory/optional,
optional linked `document_type_id`), `RfqAttachment` (→ `shared.document`). Value objects:
`Timeline{publishAt?, submissionWindow(DateRange), clarificationDeadline?, evaluationTargetDate?}`,
`EvaluationTemplateRef{EvaluationTemplateId, snapshotVersion}`. Invariants directly from
`DOMAIN-MODEL.md` §5.4 + `BRULE-030`: ≥1 `RfqItem` and a bound, version-snapshotted
`EvaluationTemplateRef` before `Approved`; `submissionCloseAt` strictly after `submissionOpenAt`
with a minimum open window (`BRULE-033`, window length `[ASSUMPTION]`); editing after `Published`
prohibited except via addenda (FEAT-10.4/`FR-CLR-004`).

**State machine.** See §1.4 above — full 13-state RFQ machine, verified against
`BUSINESS-PROCESSES.md` §3.1. Note the transition table there names the exact permission per
transition (e.g. `Draft→InternalReview` needs `rfq.submit_review`, `Approved→Published` needs
`rfq.publish`) — implement against that table directly, not a paraphrase of it.

**API surface.**
- `POST /api/v1/rfqs` (`rfq.create`, org-scoped) → `Draft`.
- `PUT /api/v1/rfqs/{ref}` items/requirements/timeline (state-gated per FEAT-07.10).
- `POST /api/v1/rfqs/{ref}/attachments` (reuses `IFileStorage`, same pattern as EPIC-05).
- `PUT /api/v1/rfqs/{ref}/evaluation-template` (`rfq.edit`, binds `EvaluationTemplateRef`).
- `POST /api/v1/rfqs/{ref}/submit-review` (`rfq.submit_review`), `POST .../review` accept/return
  (`rfq.review`/`rfq.approve`).
- `POST /api/v1/rfqs/{ref}/publish` (`rfq.publish`).
- `POST /api/v1/rfqs/{ref}/close` (early close, `rfq.close`).
- `POST /api/v1/rfqs/{ref}/cancel` (`rfq.cancel`, reason mandatory).
- All row-scoped to `IScopeContext.OrganizationId` — same enforcement pattern as the existing
  `Permissions`/`RequirePermission` kernel from EPIC-01, just with `Organization` scope instead of
  `Supplier` scope (this is a **new scope dimension** the row-scoping helper needs to support
  alongside the existing `SupplierId` one — check whether `IScopeContext` already carries
  `OrganizationId` for staff users before assuming it needs adding; per `DATABASE-MODEL.md`
  §2.1 `app_user` already has an `organization_id` column, so it likely does).

**Frontend surface.** `SCR-410`–`SCR-423` (RFQ list, create/edit wizard across
Basics/Items/Requirements/Attachments/Evaluation-template/Invitations tabs, submit-review,
manager-approve, publish dialog, RFQ workspace detail, clarifications management, cancel dialog,
close-submissions dialog). Only `SCR-420` (workspace) and none of the RFQ-specific screens have a
full `SCREEN-SPECIFICATIONS.md` entry yet — **layout/component detail for all of EPIC-07's screens
must be designed net-new**, following the same design-system tokens/RTL/a11y rules already
established (per the "cross-screen consistency checklist" quoted in the UX extraction). Flow
reference: `USER-FLOWS.md` §5 (7-step wizard: Basics → Items → Requirements → Timeline →
Evaluation-template → Attachments → Review, with autosave and a single validation gate before
submit-for-review).

**Dependencies.** Cross-epic: EPIC-21 (Category/UoM/Currency reference data, already seeded in P3),
EPIC-01 (org-scoping, permission kernel), EPIC-11-prep (EvaluationTemplate must exist to bind —
see FEAT-07.3/EPIC-11 ordering note below), EPIC-15 (review/publish notifications). Cross-phase:
Phase 3 must be complete (categories/UoM/currency exist).

**Open questions/blockers.** FEAT-07.4 (internal review/approval) is directly blocked by **OQ-004**
on the *hierarchy shape*, but not blocked on *building the single-approver interim* — build the
single-approver version now, per `ASM-040`. `BRULE-038` (post-publish amendment) is tagged
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` — the interim (addendum + re-notify) is safe to
build; a stricter "no amendment at all" or looser "free amendment" answer would each be additive/
subtractive changes, not structural ones.

**Suggested build order (vertical slices within the epic).**
1. `FEAT-07.1` RFQ authoring (header + items + requirements) — the foundational aggregate; nothing
   else in this epic or EPIC-08/09/10/11 can start without it.
2. `FEAT-07.9` opaque public reference — trivially small, do it alongside slice 1 since every other
   slice's URLs depend on it existing.
3. `FEAT-07.3` bind evaluation template — needs `EvaluationTemplate` to exist first (see EPIC-11
   ordering note: build `EvaluationTemplate` CRUD as a *small standalone slice before* this, even
   though it's formally cataloged under EPIC-11's FEAT-11.1).
4. `FEAT-07.2` attachments — thin slice, reuses EPIC-05's `IFileStorage` pattern directly.
5. `FEAT-07.4` internal review/approval, then `FEAT-07.5` publish — these two are the actual
   Phase-4 gate ("author → submit for review → approve → publish an RFQ" per `ROADMAP.md`'s exit
   criteria).
6. `FEAT-07.10` state-gated editing — implement as guard clauses alongside slices 1–5, not as a
   separate pass; retrofitting this after the fact risks missing a transition.
7. `FEAT-07.8` cancel-with-reason and `FEAT-07.6` submission-window automation can follow in either
   order — `FEAT-07.6` needs Hangfire wired (already true from Phase 0/1) and is really an EPIC-13
   concern (`FR-PWF-004`) landing here first because RFQ owns the state.
8. `FEAT-07.7` (lifecycle progression through evaluation/award stages) and `FEAT-07.11` (ERP
   mapping fields) are *stubs* at this point in the plan — `FEAT-07.7`'s real behavior is delivered
   by EPIC-11/12/13/14 later; don't over-build RFQ-side logic for those stages now.

---

### EPIC-08 — Invitations

**Scope summary.** `FR-INV-001..007`, traces to `BR-024`. Goal (verbatim): "Let buyers invite Active
suppliers to a published RFQ with status tracking, candidate suggestions, notifications, decline
handling, and strict row-scoped visibility so only invited suppliers can see RFQ detail and
propose." Primary phase: **Phase 5**.

**Domain model.** `Invitation` is a **child entity of RFQ**, not its own aggregate root — no
separate consistency boundary. Table `rfq.invitation`: `rfq_id` FK, `supplier_id` FK,
`contact_representative_id?`, `status(invitation_status)`, `access_token`, `email_sent_at?`,
`responded_at?`. `UNIQUE(rfq_id, supplier_id)` and `UNIQUE(access_token)` per
`DATABASE-MODEL.md` §2.3. Invariant (`BRULE-032`, `BRULE-043`): only `Active` suppliers may be
invited or hold an invitation used to start a proposal.

**State machine.** No top-level state machine — `InvitationStatus` is a small enum
(`invited/viewed/responding/submitted/declined`, per `DOMAIN-MODEL.md` §5.4 and
`FR-INV-001`), transitioned by simple status updates, not a guarded aggregate lifecycle. The
one real *guard* is eligibility at invite-time (Active-only) and visibility (invited-only can read
RFQ detail, `FR-INV-006`).

**API surface.**
- `POST /api/v1/rfqs/{ref}/invitations` (`rfq.invite`, bulk-capable, Active-suppliers-only guard).
- `GET /api/v1/rfqs/{ref}/invitations` (buyer status board, `FEAT-08.7`).
- `POST /api/v1/rfqs/{ref}/invitations/{supplierId}/decline` (`supplier` side, optional reason).
- Row-scoping enforcement point for `FEAT-08.6`: `GET /api/v1/rfqs/{ref}` and every proposal
  endpoint must check "does an `Invitation` exist for `scope.SupplierId` + this RFQ" before
  returning anything — this is the single highest-value negative test in this epic
  (`ROADMAP.md` Phase 5 exit criteria explicitly calls this out: "Only invited, in-scope suppliers
  can open RFQ detail (authz negative test)").

**Frontend surface.** `SCR-416` (RFQ edit — Invitations, buyer side, part of the RFQ authoring
tabs), `SCR-140` (supplier-side invitations/RFQ list), `SCR-144` (decline dialog). Flow reference:
`USER-FLOWS.md` §6 — buyer picks from a supplier directory filtered to Active + category match,
sees eligibility flags per candidate, confirms, invitations created + notified; supplier sees it in
their opportunities list and either declines (reason optional) or proceeds to a proposal (§7,
EPIC-09).

**Dependencies.** EPIC-07 (published RFQ must exist), EPIC-03 (Active suppliers must exist — this
is why Phase 5 depends on Phase 2, not just Phase 4), EPIC-15 (notify), EPIC-06 FEAT-06.3 (offering/
category data feeds `FEAT-08.2` candidate suggestions).

**Open questions/blockers.** `FEAT-08.6` (invite-only visibility) is tagged `[ASSUMPTION /
REQUIRES BUSINESS CONFIRMATION]` and maps to **OQ-021** (bid eligibility restricted by category, or
open to any Active supplier). Not phase-blocking — the interim (invite-gated visibility, open
eligibility within Active suppliers, `ASM-045`) is buildable now and is explicitly required by the
Phase 5 gate regardless of how OQ-021 eventually resolves (invite-gating and category-restriction
are two independent axes — the plan builds invite-gating now; category-restriction is the part
still open).

**Suggested build order.**
1. `FEAT-08.1` invite + status tracking — the core slice; nothing else in this epic matters without
   it.
2. `FEAT-08.6` invite-only visibility enforcement — build this **in the same PR** as slice 1, not
   after. This is a security boundary; shipping invitations without the visibility gate even
   temporarily is the kind of gap that's easy to forget to close later.
3. `FEAT-08.3` notifications with deep link.
4. `FEAT-08.4` decline-with-reason, `FEAT-08.7` status board — small, can go in either order.
5. `FEAT-08.5` late-invite-while-open, `FEAT-08.2` candidate suggestions — lowest priority (S), do
   last; `FEAT-08.2` in particular is pure UX sugar over data that already exists once EPIC-06 and
   this epic's slice 1 are done.

---

### EPIC-09 — Proposals

**Scope summary.** `FR-PRP-001..013`, traces to `BR-030..037`. Goal (verbatim): "Let invited
suppliers build and submit structured, revisable proposals with draft safety, line pricing,
commercial/technical responses, documents, submission guardrails, withdrawal, and strict
confidentiality — completing the RFQ→Invitation→Proposal triad." Primary phase: **Phase 6** (a
`ROADMAP.md`-designated **key milestone**, M4).

**Domain model.** Aggregate root `Proposal` (schema `proposal`). Entities: `ProposalItem`
(`rfq_item_id` FK, quantity, unit_price, discount_percent?, line_total (derived), lead_time_days?,
notes), `ProposalDocument` (→ `shared.document`). Value objects: `CommercialTerms{currencyCode,
paymentTerms?, incoterm?, deliveryTerms, warranty?}`, `TechnicalResponse` (answers to RFQ
`Requirement[]`, stored as `proposal.technical_response` JSONB per §1.6), `Validity(DateRange)`,
`Totals{subtotal, discountTotal, taxTotal?, grandTotal}` (always domain-derived, never
client-supplied — `BRULE-055`). **Hard uniqueness invariant**: exactly one `Proposal` per
`(SupplierId, RfqId)` — DB `UNIQUE(rfq_id, supplier_id)` **and** domain check (`BRULE-042`,
`DOMAIN-MODEL.md` §5.5). This is the single most-tested invariant in the whole epic per the
`ROADMAP.md` Phase 6 exit criteria ("a second supplier cannot see the first's contents").

**State machine.** See §1.4 — `Draft → Submitted → UnderReview → (ClarificationRequested →
Revised → UnderReview)* → Shortlisted | NotSelected → AwardOffered → Awarded | Declined`, plus
`Withdrawn` from `Draft`/`Submitted` while `SubmissionOpen`. Note precisely from
`BUSINESS-PROCESSES.md` §4.1: `Submitted → UnderReview` is triggered by `system` on RFQ reaching
`UnderEvaluation`, **not** a supplier or buyer action — this is a cross-aggregate reaction, likely
best modeled as a domain-event handler reacting to `RfqEvaluationOpened` rather than a direct call
(consistent with the "eventual consistency between aggregates via domain events" principle in
`DOMAIN-MODEL.md` §1).

**API surface.**
- `POST /api/v1/rfqs/{ref}/proposal` (`proposal.create`, invited+Active guard, idempotent —
  returns existing Draft if one exists per `STORY-09.1.1` AC1).
- `PUT /api/v1/rfqs/{ref}/proposal` (line items, terms, technical responses — autosave-friendly,
  window-gated per `BRULE-044`).
- `POST /api/v1/rfqs/{ref}/proposal/documents`.
- `POST /api/v1/rfqs/{ref}/proposal/submit` (`proposal.submit`, server-side completeness +
  window-close validation — **this is the single most safety-critical endpoint in the epic**: late
  submission must be impossible regardless of client-side clock state, per `BRULE-046` and the
  `ROADMAP.md` Phase 6 exit criteria's explicit "a late submit is rejected by the domain").
- `POST /api/v1/rfqs/{ref}/proposal/withdraw` (`proposal.withdraw`, window-gated per `BRULE-047`).
- `GET /api/v1/rfqs/{ref}/proposal` — **row-scoped to the caller's own `SupplierId`, full stop**;
  this is the confidentiality boundary (`BRULE-053`/`BRULE-088`: "no cross-supplier visibility of
  existence, content, or pricing at any time").

**Frontend surface.** `SCR-150`–`SCR-157` (proposals list, builder with line-pricing/terms/
technical/documents tabs, review-and-submit, submitted confirmation, read-only detail,
clarification-revise, withdraw dialog, award-offer outcome). `SCR-151` (Proposal Builder) has a
dependency-context note in `SCREEN-SPECIFICATIONS.md`: "Editing blocked once SubmissionClosed or
state past Submitted (except during ClarificationRequested → revise)." Flow reference:
`USER-FLOWS.md` §7 — 6-tab autosave wizard (Line pricing → Commercial terms → Technical response →
Documents → Review), one validation gate, then a second deadline check at actual submit time (the
two-gate design exists specifically to handle the case where the deadline passes *between* opening
the review tab and clicking submit).

**Dependencies.** EPIC-08 (must be invited), EPIC-07 (RFQ items/requirements to price/answer, and
the `SubmissionOpen` window from `FEAT-07.6`), EPIC-01 (supplier-scoping — this reuses the exact
row-scoping pattern already proven in EPIC-03/04/05/06), EPIC-05/`IFileStorage` (proposal
documents), EPIC-11 (blindness rules that `FEAT-09.8` must respect — build order note: `FEAT-09.8`'s
evaluator-side blindness enforcement can't be *tested* end-to-end until EPIC-11 exists, but the
supplier-side half of confidentiality — cross-supplier isolation — can and should be built and
tested as part of this epic independent of EPIC-11).

**Open questions/blockers.** None of EPIC-09's own FRs carry an `[ASSUMPTION]` tag directly, but two
real open items from the BRULE extraction land squarely here and are **not** yet in
`OPEN-QUESTIONS.md` as a numbered `OQ-*` (flagging, not inventing an answer):
(1) whether commercial/price fields can change during a `ClarificationRequested → Revised` loop
(`BRULE-050` — interim: technical-only, price locked); (2) whether a `Draft` at close truly
auto-lapses with zero recovery path, or whether some grace exists (`BRULE-052`/`ASM-043` — interim:
hard-blocked, no grace). Both are safe interim defaults to build against.

**Suggested build order.**
1. `FEAT-09.1` start + line pricing — the uniqueness invariant is the load-bearing piece; get the
   `(SupplierId, RfqId)` constraint and its negative test right first.
2. `FEAT-09.4` draft safety (autosave + confidentiality-of-drafts) — build alongside slice 1, not
   after; per `ROADMAP.md` Phase 6 exit criteria this needs a real persistence-across-reload test.
3. `FEAT-09.2` commercial terms + technical response, `FEAT-09.3` documents — straightforward
   extensions of slice 1's aggregate.
4. `FEAT-09.5` submit-with-validation — the epic's real gate; build the two-layer validation
   (completeness + window) exactly as `USER-FLOWS.md` §7 describes it, and write the late-submission
   revert-to-red test explicitly (deliberately break the window guard, confirm the exact right test
   fails, restore) given how safety-critical this endpoint is.
5. `FEAT-09.8` confidentiality — write the cross-supplier negative test now even though full
   evaluator-blindness can't be proven until EPIC-11 lands.
6. `FEAT-09.6` withdraw-while-open.
7. `FEAT-09.7` evaluation-intake/outcome transitions, `FEAT-09.9` ERP fields — stubs; real behavior
   arrives with EPIC-11/14, same pattern as EPIC-07's FEAT-07.7.

---

### EPIC-10 — Clarifications

**Scope summary.** `FR-CLR-001..006`, traces to `BR-025`. Goal (verbatim): "A fair, structured
buyer↔supplier Q&A channel per RFQ with private/published answers, asker anonymization, addenda,
window bounding, and full audit/notification." Primary phase: **Phase 5** (parallels Invitations —
`ROADMAP.md`'s epic↔phase map literally notes "4-note: parallels Invitations" for this epic).

**Domain model.** `Clarification` is a **child entity of RFQ**, table `rfq.clarification`:
`asked_by_supplier_id?`, `question_ar/_en`, `answer_ar/_en?`,
`visibility(clarification_visibility: PrivateToAsker | PublishedToAll)`, `asked_at`, `answered_at?`,
`answered_by?`. No separate aggregate, no state machine — a thread with a visibility toggle. The
**one structurally distinct piece** here is `FEAT-10.4` (RFQ addendum), which is the formal
exception carved into EPIC-07's `FEAT-07.10` state-gated editing rule ("locked after Published
except addenda") — this feature technically belongs to the RFQ aggregate's editing rules even
though it's catalogued under EPIC-10.

**State machine.** None. Guards only: post outside the clarification window is refused
(`FR-CLR-005`, `BRULE-` implied by the RFQ `Timeline.clarificationDeadline?` field).

**API surface.**
- `POST /api/v1/rfqs/{ref}/clarifications` (supplier asks, `clarification.ask`, window-gated).
- `POST /api/v1/rfqs/{ref}/clarifications/{id}/answer` (buyer answers, `clarification.answer`,
  chooses `PrivateToAsker` or `PublishedToAll`).
- `POST /api/v1/rfqs/{ref}/addenda` (`FEAT-10.4`, notifies all invited suppliers, records on RFQ
  timeline).
- `GET /api/v1/rfqs/{ref}/clarifications` — visibility-filtered per caller (asker sees own private
  thread + all published; other suppliers see only published, asker anonymized).

**Frontend surface.** `SCR-421` (buyer clarifications management/triage), `SCR-143` (supplier Q&A).
Flow reference: `USER-FLOWS.md` §8.1 (RFQ public Q&A) and §8.2 (proposal-clarification, which is
actually the `ClarificationRequested → Revised` proposal-state loop, not this epic's `Clarification`
entity — **note the naming overlap is real and worth flagging**: "clarification" means two different
things in this codebase's docs — an RFQ-level public Q&A entity (this epic) and a proposal-level
private revision-request state (EPIC-09's `ClarificationRequested` state). Keep these visibly
distinct in code naming (e.g. `RfqClarification` vs. the `Proposal.ClarificationRequested` state)
to avoid the exact kind of confusion the docs themselves exhibit.

**Dependencies.** EPIC-07 (published RFQ, timeline window), EPIC-08 (must be invited to ask),
EPIC-15 (notify), and it feeds back into EPIC-07's edit-lock exception (`FEAT-07.10`/`FEAT-10.4`).

**Open questions/blockers.** `FEAT-10.2` (private vs. publish-to-all default) is tagged
`[ASSUMPTION]` and maps directly to **OQ-008**. Not phase-blocking — build the interim (private
default, `ASM-044`) now.

**Suggested build order.**
1. `FEAT-10.1` post questions + `FEAT-10.5` window bounding — build together, the guard is trivial.
2. `FEAT-10.2` answer private/publish + `FEAT-10.3` published-thread-anonymized — the core value of
   the epic; anonymization is a display-layer concern (never store the asker's identity as visible
   to non-buyer roles on a published item) not a data-model concern (the row still has
   `asked_by_supplier_id` for the buyer's own audit/reference).
3. `FEAT-10.4` addenda — do this once EPIC-07's `FEAT-07.10` state-gating exists, since it's the
   exception path to that rule.
4. `FEAT-10.6` audit/notify — should already fall out of slices 1–3 if the standing "every
   transition is audited" convention is followed; treat this as a verification pass, not new work.

---

### EPIC-11 — Evaluation

**Scope summary.** `FR-EVL-001..011` + `FR-ADM-005` (evaluation templates), traces to `BR-040..046`.
Goal (verbatim): "Multi-evaluator, independent-then-consolidated evaluation against the RFQ's
weighted criteria with thresholds, lock-on-submit, permissioned override, and finalize — desktop/
tablet optimized and RTL-correct." Primary phase: **Phase 7**.

**Domain model.** Two aggregates: `EvaluationTemplate` (portal-only, schema `evaluation`, entity
`Criterion{name, dimension(Technical|Commercial|Compliance|Delivery), weight%, maxScore, threshold?,
scoringType(Numeric|Scale|Boolean|Formula), guidance, sortOrder}`) and `Evaluation` (the per-RFQ
scoring instance: `EvaluationAssignment[]`, `EvaluatorScore[]`, `ConsolidatedResult`). Invariant
from `DOMAIN-MODEL.md` §5.6/`BRULE-065`: Σ`Criterion.weight` = 100% before a template can go
`Active`; a template referenced by any live RFQ is immutable (edits create a new `version` — the
RFQ holds the frozen `snapshotVersion`). Invariant on `Evaluation` (`DOMAIN-MODEL.md` §5.7):
`evaluation.rfq_id` is `UNIQUE` — one evaluation per RFQ; an evaluator may submit scores only for
proposals they're assigned and only for criteria in the RFQ's *snapshotted* template (never the
live template, which may have moved on).

**State machine.** See §1.4 — `NotStarted → Assigned → InProgress → EvaluatorSubmitted →
Consolidated → Finalized`, with `Consolidated → InProgress` re-open (reason mandatory,
`procurement_manager` only). `EvaluationTemplate` has its own tiny separate lifecycle:
`Draft → Active → Archived`.

**API surface.**
- `POST /api/v1/evaluation-templates` / `PUT .../{id}` (`admin.reference.manage` or
  `procurement_manager`-equivalent, per `FR-ADM-005` — **this must exist and be usable before
  EPIC-07's FEAT-07.3 can bind anything**, hence the cross-epic ordering note under EPIC-07's
  build order above).
- `POST /api/v1/rfqs/{ref}/evaluation/assignments` (`evaluation.assign`, creates
  `EvaluationAssignment[]`).
- `GET /api/v1/rfqs/{ref}/evaluation/my-scores` (`evaluation.score`, **row-scoped to
  `scope.UserId` as evaluator** — this is the API-level blindness enforcement point for OQ-005;
  there is no "get all evaluators' scores" endpoint available to an `evaluator` role at all, only
  to `procurement_officer`/`procurement_manager` and only from `Consolidated` onward).
- `PUT /api/v1/rfqs/{ref}/evaluation/scores` (save partial, per-criterion, range-validated against
  `[0, Criterion.max]`).
- `POST /api/v1/rfqs/{ref}/evaluation/submit` (`evaluation.submit`, locks that evaluator's scores;
  only when all their assigned proposals are fully scored).
- `POST /api/v1/rfqs/{ref}/evaluation/consolidate` (`evaluation.consolidate`, requires all assigned
  evaluators `EvaluatorSubmitted` or an explicit quorum override).
- `POST /api/v1/rfqs/{ref}/evaluation/finalize` (`evaluation.finalize`).
- `POST /api/v1/rfqs/{ref}/evaluation/reopen` (`evaluation.reopen`, reason mandatory, audited).

**Frontend surface.** `SCR-434`–`SCR-436` (buyer: evaluation setup/committee, progress monitor,
consolidated results & shortlist) and `SCR-500`–`SCR-505` (evaluator: dashboard/assignments, brief,
**scoring workspace `SCR-502`** — full spec exists, split-pane proposal-evidence vs. scoring-panel
layout, tablet-first-class, running weighted total, threshold-breach flagging; submit dialog,
read-only submitted view). `SCR-502`'s spec explicitly states peers' scores are never shown and
validation requires a comment when below threshold (`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`
on the comment-required rule specifically).

**Dependencies.** EPIC-09 (submitted proposals must exist — `Submitted → UnderReview` is triggered
by RFQ reaching `UnderEvaluation`), EPIC-07 (evaluation template bound at RFQ approval time, and the
RFQ must actually reach `SubmissionClosed → UnderEvaluation`), EPIC-01 (evaluator role/scoping).

**Open questions/blockers.** This is the epic most directly shaped by **OQ-005** (blind scoring
enforcement — non-blocking, build the interim now, but build it as hard API-level scoping as
described above, not UI convention) and **OQ-009** (two-envelope — **blocking** for the *eventual*
structural shape, but not for building the single-mixed-template interim now, per §1.2's guidance).
Also carries two smaller open items from the BACKLOG/BRULE extraction: `FEAT-11.7` non-responding-
evaluator/quorum handling (`BRULE-066`, priority C — safe to defer to a later slice, it's explicitly
low-priority in the backlog itself), and the consolidation aggregation formula itself
(`BRULE-063`/`ASM-051` — interim: simple average per criterion, then weighted — `[ASSUMPTION /
REQUIRES BUSINESS CONFIRMATION]`).

**Suggested build order.**
1. `FEAT-11.1` EvaluationTemplate CRUD — build this **first**, ahead of everything else in this
   epic and even ahead of finishing EPIC-07, since EPIC-07's FEAT-07.3 needs a real template to bind
   to. This is the one place in the whole plan where a later epic's feature needs to land before an
   earlier epic's feature can be tested end-to-end.
2. `FEAT-11.2` assign evaluators.
3. `FEAT-11.3` blind independent scoring — build the API-level row-scoping (the OQ-005 enforcement
   point) as part of this slice, with an explicit negative test (evaluator B's token cannot read
   evaluator A's scores) written alongside it, not bolted on later.
4. `FEAT-11.4` weighted computation + thresholds.
5. `FEAT-11.5` submit + lock.
6. `FEAT-11.6` consolidate + finalize — the epic's real gate; `ROADMAP.md` Phase 7 exit criteria:
   "Assign 2+ evaluators → each scores blind → submit locks scores → consolidate → finalize;
   below-threshold flags/disqualifies per template; override requires permission + audit."
7. `FEAT-11.8` evaluator UX/audit polish, `FEAT-11.7` non-responding-evaluator handling — last,
   lower priority.

---

### EPIC-12 — Comparison

**Scope summary.** `FR-CMP-001..006`, traces to `BR-044`. Goal (verbatim): "Side-by-side proposal
comparison with best-per-line/threshold highlighting, multi-currency normalization,
blindness-respecting visibility, export for the award file, and responsive/RTL tables." Primary
phase: **Phase 7** (same phase as Evaluation — they gate together per the ROADMAP exit criteria).

**Domain model.** **None — this is a read-only derived view**, not an aggregate. It queries
`Proposal` (line prices, terms, technical responses) joined with `Evaluation.ConsolidatedResult`
(scores, ranking) for a given RFQ. No new persistence beyond what EPIC-09/11 already store. This is
the one epic in this plan with literally nothing to add to `DATABASE-MODEL.md`.

**State machine.** None (derived view). The only lifecycle-adjacent behavior is *visibility gating*:
the comparison must honor evaluation blindness until `Finalized` (`FR-CMP-004`) — i.e. before
`Finalized`, even `procurement_officer`/`procurement_manager` should not see individual evaluator
scores, only what's appropriate at the current `Evaluation.State` (this mirrors the same blindness
rule as EPIC-11, applied to a different read surface).

**API surface.**
- `GET /api/v1/rfqs/{ref}/comparison` — a read query handler (no domain command), joins
  `proposal.proposal`/`proposal_item` with `evaluation.consolidated_result` where the evaluation
  state permits; row-scoped to the caller's `OrganizationId`.
- `GET /api/v1/rfqs/{ref}/comparison/export` (`FEAT-12.5`, PDF/print, priority C — lowest in this
  epic, defer).

**Frontend surface.** `SCR-432` **has a full spec**: frozen-header matrix (criteria/line items as
rows, proposals as columns), three groups (Commercial/Requirements/Evaluation), best-value
highlighting via icon+text (never color alone — explicit a11y requirement in the spec), mobile
degrades to one-supplier-at-a-time card view with a "differences only" filter. `USER-FLOWS.md` §10
adds: toggle views (Technical/Commercial/Combined), threshold-breach flagging with optional
"disqualify + reason" action, and an explicit note that **Ministry does not see this commercial
matrix** (visibility follows a separate `gov.commercial.read`-style permission — this ties directly
to **OQ-001**, which is outside this plan's scope but worth flagging as a shared concern with the
later Ministry epic, EPIC-18).

**Dependencies.** EPIC-09 (proposals to compare), EPIC-11 (scores to show, and the blindness state
to respect) — this epic cannot meaningfully start until both are substantially built, since it has
no data of its own.

**Open questions/blockers.** `FEAT-12.3` (multi-currency normalization) is tagged
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` and maps to **OQ-007** — interim: no FX conversion,
amounts shown in entered currency (`ASM-030`). This is genuinely safe to build now (the interim is
"don't normalize," which is strictly simpler than the eventual real behavior) but means
`FEAT-12.3`'s "normalize to a display currency with the rate shown" is **not buildable as specified
today** — build the comparison matrix showing native currencies side-by-side instead, with
normalization as a deferred enhancement once OQ-007 resolves.

**Suggested build order.**
1. `FEAT-12.1` comparison matrix (commercial + requirements groups only — no scores yet, since this
   can be built and tested against submitted proposals alone, before EPIC-11 fully lands).
2. `FEAT-12.4` blindness/permission respect — add as soon as the Evaluation group is wired in; don't
   ship the Evaluation column without this gate.
3. Wire in the Evaluation group once EPIC-11's `ConsolidatedResult` exists.
4. `FEAT-12.2` best/threshold highlighting.
5. `FEAT-12.6` responsive/RTL table polish.
6. `FEAT-12.3` currency (deferred to native-currency-only per above), `FEAT-12.5` export — last,
   lowest priority (S and C respectively).

---

### EPIC-13 — Procurement Workflow (orchestration)

**Scope summary.** `FR-PWF-001..005`, traces to `BR-` (workflow/governance rows, no dedicated
BR-0xx block of its own — it's cross-cutting orchestration language). Goal (verbatim): "Present the
RFQ lifecycle as a guided, gated workspace binding RFQ, Invitations, Proposals, Clarifications, and
Evaluation, with stage-gate prerequisites, timeline automation, and concurrency handling." Primary
phase: **Phase 8** (co-delivered with Award — `ROADMAP.md` calls Phase 8 the "**Key milestone —
core procurement value fully delivered end-to-end**").

**Domain model.** **None of its own** — this epic is explicitly an orchestration/UX layer over RFQ
+ Invitation + Proposal + Clarification + Evaluation, all of which are built by EPICs 7–11. Nothing
new in `DATABASE-MODEL.md` for this epic specifically. This is the second epic in the plan (after
Comparison) with no new persistence.

**State machine.** None of its own; it *reads and gates* the RFQ state machine (§1.4) plus checks
cross-aggregate preconditions the RFQ aggregate alone can't see (e.g. "is Evaluation `Finalized`" is
a fact that lives on the `Evaluation` aggregate, not `RFQ` — `FR-PWF-002`'s stage gate "no
recommendation before evaluation Finalized" is inherently a cross-aggregate check, which is exactly
why it's modeled as an orchestration concern rather than folded into the RFQ aggregate itself,
consistent with `DOMAIN-MODEL.md` §1's "aggregates are separate consistency boundaries" principle).

**API surface.** Mostly **read-oriented**: `GET /api/v1/rfqs/{ref}/workspace` — a composed DTO
pulling current `RfqState`, permitted next actions (computed server-side from state + caller's
permissions, never left to the client to guess), and any blockers (e.g. "cannot open evaluation:
0 submitted proposals"). The one *write* surface unique to this epic is concurrency-conflict
handling (`FEAT-13.5`): every state-changing endpoint across EPIC-07/09/11/14 needs to return a
structured `409` on an `xmin` mismatch that the frontend can render as a localized "someone else
changed this, reload?" prompt — this is a cross-cutting concern implemented once and reused, not a
new endpoint of its own.

**Frontend surface.** No dedicated `SCR-*` IDs of its own found in the extraction — it *is*
`SCR-420` (RFQ workspace/detail), already catalogued under EPIC-07's frontend surface. This
confirms the "orchestration, not a separate product surface" framing: EPIC-13's deliverable is
largely the intelligence behind the RFQ workspace screen EPIC-07 already builds, not a new screen.

**Dependencies.** All of EPIC-07/08/09/10/11 — this genuinely cannot be meaningfully finished before
those are built, though the *concurrency-handling* piece (`FEAT-13.5`) and the *timeline-automation
resilience* piece (`FEAT-13.4`) can and should be built incrementally alongside each of those epics
rather than saved for a dedicated pass at the end (this is why `ROADMAP.md`'s epic↔phase map shows
EPIC-13 "seeded" starting Phase 4, cross-cutting into 4-7, formally advanced in Phase 8).

**Open questions/blockers.** None of its own beyond what it inherits from the epics it orchestrates.

**Suggested build order.** This epic doesn't have a clean linear order the way the others do — it's
better framed as **three concerns threaded through the other epics' build order**, landing formally
in Phase 8:
1. `FEAT-13.4` (durable/idempotent scheduling) — build this pattern once, in EPIC-07's
   `FEAT-07.6` slice (submission window automation), then reuse it for every later scheduled
   transition (clarification deadlines, evaluation reminders) rather than reinventing per-epic.
2. `FEAT-13.5` (`xmin` conflict → localized prompt) — build this once as a shared API/UI pattern
   during EPIC-07's first write endpoint, then every subsequent epic's write endpoints inherit it
   for free.
3. `FEAT-13.1`/`FEAT-13.2`/`FEAT-13.3` (guided workspace, stage gates, action audit) — this is the
   genuine Phase-8 deliverable: once EPIC-07/09/11/14 all exist, build the composed
   `GET /rfqs/{ref}/workspace` view and the stage-gate guard clauses that check cross-aggregate
   preconditions before allowing a transition. This is realistically the *last* piece of new work
   in Phase 8, built alongside or just after EPIC-14.

---

### EPIC-14 — Award

**Scope summary.** `FR-AWD-001..008`, traces to `BR-050..055`. Goal (verbatim): "Complete the loop:
recommendation → approval → award → non-winner handling → immutable award file → Outbox award
event to ERP (PO), never blocking on ERP." Primary phase: **Phase 8** (co-delivered with EPIC-13,
same "core procurement value fully delivered end-to-end" milestone).

**Domain model.** Aggregate root `Award` (schema `award`, `UNIQUE(rfq_id)` — one award per RFQ).
Entities: `Recommendation` (justification, references `ConsolidatedResult`), `Approval[]`
(ordered chain: `step_no`, `approver_user_id`, `decision`, `comment`, `decided_at` —
**already modeled as an array/chain in the schema**, per §1.2's note on OQ-004), `AwardDecision`
(`winning_proposal_id`). Value object `ExternalPurchaseOrderRef{externalId?, status, issuedAt?}`.
Invariants (`DOMAIN-MODEL.md` §5.8, `BRULE-071`/`075`/`080`): references only `Shortlisted`,
threshold-passing proposals; `Awarded` requires the full `Approval` chain resolved to `Approved`;
winning supplier must still be `Active` at approval time (domain guard, not UI-only); at most one
winning award per RFQ (split-award policy `[ASSUMPTION]`, `BRULE-080`).

**State machine.** See §1.4 — `Recommended → PendingApproval → Approved | Rejected → Awarded →
ErpPoRequested → ErpPoSynced` (with `ErpPoFailed` retry loop), `Rejected → Recommended` rework. Note
the **award-to-ERP sub-states** (`ErpPoRequested`/`ErpPoSynced`/`ErpPoFailed`) are part of the
canonical Award state machine per `BUSINESS-PROCESSES.md` §6, not a separate "integration" concern
bolted on — model them as real `AwardState` enum values now (matching `DATABASE-MODEL.md`'s
`award_state` enum), even though the *actual* ERP adapter behind `ErpPoRequested → ErpPoSynced` is
Phase 11 work (a stub adapter is sufficient here, per the established pattern already used for
`SupplierApproved` in EPIC-02/03).

**API surface.**
- `POST /api/v1/rfqs/{ref}/award/recommend` (`award.recommend`, requires Evaluation `Finalized` +
  winner passes thresholds + justification).
- `POST /api/v1/rfqs/{ref}/award/route` (`award.recommend`, creates `Approval` chain per the
  authority matrix — single-approver interim per OQ-004/`ASM-041`).
- `POST /api/v1/rfqs/{ref}/award/approve` / `.../reject` (`award.approve`, **segregation of duties
  enforced at the API policy layer**: approver ≠ recommender, `BRULE-073` — this is a real,
  testable authz rule, not just a UI hint. Reject requires mandatory reason).
- `POST /api/v1/rfqs/{ref}/award/execute` (on final approval: sets `AwardDecision`, transitions
  winning `Proposal → AwardOffered/Awarded`, others → `NotSelected`, writes the `Award` transactional
  Outbox row **in the same DB transaction** as the state change — this is the one place in this
  entire plan where getting the Outbox-in-same-transaction discipline right matters most, since it's
  the literal integration boundary to ERP).
- `GET /api/v1/rfqs/{ref}/award` (award file — justification + comparison snapshot, immutable,
  `FEAT-14.7`).

**Frontend surface.** `SCR-437`–`SCR-440` (recommendation composer, award approval workspace,
award decision & notify, award outcome/PO-sync-status). None of these four has a detailed
`SCREEN-SPECIFICATIONS.md` entry — design net-new, following the same design-system/RTL/a11y rules.
`USER-FLOWS.md` §11 gives the step sequence directly, including the supplier-side
accept/decline-offer branch (`[ASSUMPTION: acceptance required]` — i.e. whether the winning
supplier must actively accept, or the award is final on approval alone, is itself unconfirmed and
should be flagged alongside OQ-004 rather than assumed silently either way) and the loser-
notification branch ("Notify losers" → `NotSelected`, no scores disclosed per default, `BRULE-082`).

**Dependencies.** EPIC-11 (Evaluation must be `Finalized`), EPIC-09 (the winning `Proposal` must
exist and be `Shortlisted`), EPIC-13 (RFQ-side stage gating that keeps `AwardApproval` from being
reachable before `Recommendation` is real), and forward-looking to Phase 11 (the real ERP PO
adapter behind the Outbox stub built here).

**Open questions/blockers.** This epic carries the **sharpest edge of OQ-004** — `FEAT-14.2`
("route for approval... single approver, configurable hierarchy `[ASSUMPTION]`") is explicitly
named in `OPEN-QUESTIONS.md` itself as blocking: "award slice cannot finalize its approval model
without this." Build the single-approver interim now (buildable), but do **not** consider EPIC-14
fully closed/production-ready until OQ-004 resolves and the authority-matrix/threshold-routing
logic (`BRULE-072`/`074`) is confirmed and implemented — this is the one epic in the whole plan
where "ship the interim" and "epic done" are genuinely different bars, because award decisions are
the highest-stakes, most-audited transitions in the entire system. Also carries the
supplier-acceptance-required question noted above, and `BRULE-080`'s split-award-policy
`[ASSUMPTION]` (interim: single winner only, no split — the simpler and safer default).

**Suggested build order.**
1. `FEAT-14.1` produce recommendation — needs `EPIC-11` finalized evaluation to exist first; this
   is the true entry point of the epic.
2. `FEAT-14.2` route for approval (single-approver interim) + `FEAT-14.3` approve/reject — build
   together, including the segregation-of-duties negative test (`BRULE-073`) as a first-class part
   of this slice, not an afterthought.
3. `FEAT-14.4` issue award + notify — the state-fan-out to `Proposal` (winner→`Awarded`,
   others→`NotSelected`) and the loser-notification path.
4. `FEAT-14.5` Outbox → ERP PO stub — build the transactional-Outbox-write discipline correctly now
   even though the real adapter is Phase 11; write a test proving award succeeds even when the
   (stub) adapter "fails", per the canonical "portal never blocks on ERP" rule.
5. `FEAT-14.6` RFQ closure (`AwardApproval → Awarded → Completed`) — this is really EPIC-13's stage-
   gating applied to the terminal transition; build it alongside EPIC-13's Phase-8 workspace pass.
6. `FEAT-14.7` immutable award file — last; depends on `FEAT-12.5`'s comparison-snapshot export
   existing in some form to attach.

---

## Step 3 — Overall sequencing across all 8 epics

### 3.1 The roadmap's own phase order (authoritative, from `ROADMAP.md` §3)

```
P4 RFQ authoring/review/publish  →  P5 Invitations + Clarifications  →  P6 Proposals
  →  P7 Evaluation + Comparison  →  P8 Procurement Workflow + Award
```

Each arrow is a real dependency, not just a suggested order — `ROADMAP.md`'s phase-dependency
diagram marks two of them explicitly: "P4 -.RFQ exists.-> P6" (Phase 6 needs a published RFQ) and
"P8 -.award event.-> P11" (the later ERP-integration phase needs a real award to have happened).
The plan in Step 2 above follows this exactly: EPIC-07 → (EPIC-08 ∥ EPIC-10) → EPIC-09 →
(EPIC-11 ∥ EPIC-12, with EPIC-12 trailing EPIC-11 since it has no data of its own) → (EPIC-13 ∥
EPIC-14, co-delivered as one milestone).

### 3.2 What's genuinely parallelizable within this sequence

- **EPIC-08 (Invitations) and EPIC-10 (Clarifications)** — `ROADMAP.md` itself calls these out as
  parallel ("4-note: parallels Invitations"), both landing in Phase 5, both children of the RFQ
  aggregate with no dependency on each other.
- **EPIC-11 (Evaluation template CRUD specifically) can start during Phase 4**, ahead of its
  formal Phase 7 slot, because EPIC-07's `FEAT-07.3` needs a real `EvaluationTemplate` to bind to.
  This is the one deliberate exception to strict phase-sequential ordering in this plan — flagged
  explicitly under both EPIC-07's and EPIC-11's build orders above.
- **EPIC-13's cross-cutting concerns** (durable scheduling, `xmin` conflict handling) are built
  incrementally inside EPIC-07/09/11's own slices, not as a separate Phase-8-only effort — this
  matches `ROADMAP.md`'s own "cross-cutting from day one" principle (§1) applied to this specific
  epic's split between "seeded early" and "formally advanced in Phase 8."
- **EPIC-12 (Comparison)'s commercial+requirements view** can be built and demoed against Phase 6
  proposal data alone, before Phase 7's Evaluation work lands — only the Evaluation column of the
  matrix needs to wait.

### 3.3 What's blocked on a real business decision vs. buildable now regardless

**Genuinely blocked on a decision** (build the interim, but treat the epic as provisionally closed,
not finally closed, until resolved):

| Blocked concern | Epic(s) | Question | Interim built now |
|---|---|---|---|
| Approval hierarchy shape (single vs. multi-level vs. threshold-routed vs. committee) | EPIC-07 (FEAT-07.4), EPIC-14 (FEAT-14.2) | **OQ-004** | Single configurable approver (`ASM-040/041`) |
| Two-envelope vs. single-mixed evaluation | EPIC-11, EPIC-12 | **OQ-009** | Single mixed weighted template (`ASM-052`), built with `Criterion.dimension` already present so a later phased-gate is additive |
| Blind-scoring enforcement mechanism (hard API-level vs. convention) | EPIC-11 | **OQ-005** | Hard API-level row-scoping — build the *stricter* interim so a looser future answer only relaxes, never re-hardens, a control |

**Not blocking, safe to build the interim and move on** (confirm before final launch, per
`ROADMAP.md`'s own Phase-12 hardening framing, but no epic here needs to wait):

- OQ-007 (FX/cross-currency) — EPIC-12, native-currency display only.
- OQ-008 (clarification broadcast default) — EPIC-10, private-by-default.
- OQ-021 (bid eligibility by category) — EPIC-08, open-to-any-Active-supplier.
- The un-numbered items surfaced in this pass (price-revision-during-clarification, tie-break rule,
  supplier-acceptance-required-for-award, split-award policy) — all have safe, simpler-than-the-
  likely-real-answer interims already described per-epic above.

**Genuinely buildable now with zero dependency on any open question:** the entire structural
skeleton of all 8 epics — aggregates, state machines, permission kernel, row-scoping, audit,
Outbox-in-transaction discipline, RTL/a11y/localization on every screen. None of the three blocking
OQs change *whether* these epics get built or *what aggregates they need* — they only change the
internal shape of specific rules (approval routing logic, evaluation sequencing, blindness
strictness) inside already-correctly-modeled aggregates. This is by design: `ASM-040`, `ASM-041`,
`ASM-050`, and `ASM-052` were each chosen, per their own stated rationale, specifically because the
domain model already supports the eventual real answer as an extension rather than a rewrite
(ordered `Approval[]` chain already exists; `Criterion.dimension` already exists; blind-scoring is
already API-enforced rather than convention-based).

### 3.4 Recommended session-level sequencing for the next several build sessions

1. **Session block 1** — EPIC-11 FEAT-11.1 (EvaluationTemplate CRUD) as a standalone slice, then
   EPIC-07 in full (FEAT-07.1/07.9/07.3/07.2, then 07.4/07.5, then 07.10/07.6/07.8). This closes
   the Phase 4 gate.
2. **Session block 2** — EPIC-08 and EPIC-10 together (both Phase 5, both children of RFQ, no
   inter-dependency). Closes the Phase 5 gate.
3. **Session block 3** — EPIC-09 in full. This is a `ROADMAP.md`-designated key milestone (M4) and
   the largest single epic by AC count in this plan — likely worth its own dedicated session block
   rather than combining with anything else. Closes the Phase 6 gate.
4. **Session block 4** — EPIC-11 (remaining FEAT-11.2 through 11.8) and EPIC-12 together. Closes
   the Phase 7 gate (`ROADMAP.md` M5).
5. **Session block 5** — EPIC-14 and EPIC-13 together. This is the `ROADMAP.md`-designated
   milestone M6 ("register→onboard→RFQ→invite→propose→evaluate→award" fully demoable
   end-to-end) — the natural point to also run a full cross-epic re-verification pass, the same
   discipline already applied earlier tonight when Epics 3/6 were re-verified together after the
   RolesPage fix.

Each session block should end the way tonight's EPIC-03/06 sessions did: full fresh test run
(backend integration + unit + architecture, frontend unit + build), revert-to-red proof on the
riskiest new logic in that block (late-submission rejection in EPIC-09; segregation-of-duties in
EPIC-14; blind-scoring row-scoping in EPIC-11), and real browser verification of the actual
end-to-end flow that block completes — not just unit-level correctness.
