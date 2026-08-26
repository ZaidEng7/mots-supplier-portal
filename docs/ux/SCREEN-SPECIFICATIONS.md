# MOTS Supplier Portal — Screen Specifications (Key Screens)

> **Status:** Baseline v1 · **Owner:** UX + Product · **Date:** 2026-08-26
> **Canonical sources:** [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md)
> **Related UX docs:** [`SCREEN-INVENTORY.md`](./SCREEN-INVENTORY.md) ·
> [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md)

Detailed specifications for the **twelve highest-value screens**. Each spec references its
`SCR-###` ID from the [Screen Inventory](./SCREEN-INVENTORY.md) and the component library and tokens
in [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md). Every screen is **Arabic-first (RTL default)**, English
(LTR) secondary, responsive, WCAG 2.2 AA, and permission-guarded at the API with UI affordance-hiding
only.

## Conventions used in every spec

- **Regions** are described in **logical** terms (`inline-start` / `inline-end` / `block-start`),
  never physical left/right — layout mirrors automatically under RTL via CSS logical properties.
- **Components** are drawn from the bespoke design system (Tailwind v4 + Radix primitives): `AppShell`,
  `PageHeader`, `Card`, `DataTable` (TanStack Table), `StatTile`, `StatusBadge`, `Timeline`, `Stepper`,
  `FormField`, `FileDropzone`, `Drawer`, `Dialog`, `Toast`, `EmptyState`, `SkeletonList`, `ErrorPanel`,
  `Money` (tabular figures), `Combobox`, `Tabs`, `Banner`.
- **Forms:** React Hook Form + Zod; the same Zod schema backs client validation and mirrors the
  server FluentValidation contract. Focus moves to the first invalid field; errors are announced via
  `aria-live="polite"`.
- **Data:** TanStack Query for server state; optimistic updates only where safely reversible; writes
  respect `RowVersion` optimistic concurrency (409 → reconcile prompt).
- **Numbers/money:** `Money` renders `SYP` default (configurable), tabular figures, Western-Arabic
  digits by default `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`, switchable to Eastern-Arabic.
- **A11y baseline (all screens):** landmark regions, visible focus ring, 44px min touch target,
  `prefers-reduced-motion` respected, color never the sole signal (icon + text with every status).

---

## 1. Supplier Dashboard — `SCR-120`

**Purpose.** The supplier's home: a single glance tells them whether their account is healthy, what
needs action (documents, invitations, proposal deadlines), and where to go next.

**Persona(s).** `supplier_admin`, `supplier_user` (delegated — sees only permitted widgets).

**Entry points.** Post-login default for approved suppliers; `AppShell` logo/home; onboarding
"submitted" and approval notifications deep-link here.

**Layout (regions).**
- `block-start` **PageHeader**: greeting + company name, `StatusBadge` (Active / UnderReview /
  Suspended), global language + RTL toggle (in shell).
- **Action-required strip** (full-width `Banner` row, conditional): expiring/rejected documents,
  invitations closing soon, clarifications answered, award offers — each is a dismissible actionable
  chip linking to the relevant screen.
- **KPI row** (`StatTile` ×4): Open invitations · Draft proposals · Submitted proposals ·
  Documents needing attention.
- Two-column body (stacks on mobile):
  - `inline-start` (wider): **Invitations & deadlines** list (top 5, `DataTable` compact) → `SCR-140`;
    **Active proposals** list with state + validity countdown → `SCR-150`.
  - `inline-end`: **Profile & document health** `Card` (completeness meter, next required doc) →
    `SCR-121`/`SCR-130`; **Recent notifications** (top 5) → `SCR-900`.

**Components used.** `PageHeader`, `Banner`, `StatTile`, `DataTable`, `StatusBadge`, `Timeline`
(mini), `ProgressMeter`, `Card`, `EmptyState`, `SkeletonList`.

**Data shown.** Supplier status; counts by proposal/invitation state; document attention count
(ExpiringSoon/Expired/Rejected); nearest deadlines; profile completeness %; recent notifications.
All scoped to the caller's `SupplierId`.

**Actions / permissions.** View invitation (`rfq.read`), open proposal (`proposal.read`), upload
document (`document.upload`), manage team (`supplier.users.manage`, admin only). Affordances hidden
when the permission is absent; API re-checks.

**Validation.** None (read surface); action chips validate on their target screen.

**States.**
- *Loading:* `SkeletonList` for KPI tiles + lists; header shows name immediately from session.
- *Empty (newly approved, no activity):* `EmptyState` "No invitations yet — complete your profile so
  buyers can find you", CTA to `SCR-121`.
- *Not-yet-approved:* dashboard replaced by onboarding progress banner linking to `SCR-100`.
- *Error:* `ErrorPanel` per widget (isolated failures don't blank the page) + retry.
- *ERP-degraded:* subtle `Banner` (`SCR-045`); no functional impact.

**Mobile behavior.** Single column; KPI tiles become a horizontally scrollable 2×2; bottom tab nav
(Home · RFQs · Proposals · Documents · More); action strip collapses to a count badge.

**RTL notes.** Progress meter fills from inline-start; deadline countdowns and `Money` keep digits
LTR-internally while flowing RTL; trend/clock icons mirror.

---

## 2. Onboarding Wizard — Documents Step — `SCR-106`

**Purpose.** Collect all required supplier documents with clear per-document status, so the submission
is complete and reviewable. Representative of the wizard's step pattern (`SCR-100`–`SCR-107`).

**Persona(s).** `supplier_admin`.

**Entry points.** `SCR-100` onboarding hub; "Documents" step in `Stepper`; deep link from
`SCR-109` info-requested when a document was rejected.

**Layout (regions).**
- `block-start`: `Stepper` (Company · Contacts · Addresses · Banking · Offerings · **Documents** ·
  Review) with completion ticks; step title + short helper text.
- **Body**: a `Card` per required `DocumentType`, each showing type name, requiredness, description/
  format hint (PDF/JPG, max size), `StatusBadge` (Required / Uploaded / UnderReview / Approved /
  Rejected), and a `FileDropzone` or uploaded-file row with replace/remove.
- `block-end`: wizard footer — Back · Save & exit · Next (Next enabled only when required docs are
  Uploaded/Approved; optional docs never block).

**Components used.** `Stepper`, `Card`, `FileDropzone`, `FilePreviewRow`, `StatusBadge`, `Tooltip`,
`Toast`, `WizardFooter`, `Dialog` (replace confirm).

**Data shown.** Required + optional `DocumentType`s (admin-configured, `SCR-711`); per-type upload
state, filename, size, uploaded date, rejection reason (if any), expiry (if set on review).

**Actions / permissions.** Upload/replace/remove document (`document.upload`), continue/submit wizard
(`supplier.onboarding.edit`). `supplier_user` typically read-only here unless granted.

**Validation.** Client: accepted MIME types, max file size, required-type completeness before Next.
Server: virus/type re-check, size, storage via `IFileStorage`. Errors are per-card, `aria-live`.
No invented Syrian document rules — types and expiry are data-driven and tagged
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` where a rule is unknown.

**States.**
- *Loading:* skeleton cards.
- *Empty:* if no document types configured, guidance to contact support (should not happen in prod).
- *Uploading:* per-file progress bar; other cards remain interactive; navigation guarded until upload
  settles.
- *Rejected (from a prior review):* card highlighted (danger accent + icon), reason shown, replace CTA.
- *Error:* upload failure inline with retry; the file input value is preserved.

**Mobile behavior.** Cards full width; `FileDropzone` becomes a large tap-to-pick + camera capture
option; sticky wizard footer; `Save & exit` always reachable.

**RTL notes.** Dropzone icon/text centered (direction-neutral); progress bars fill inline-start;
filenames may be LTR (isolated with `bdi`) inside RTL layout.

---

## 3. Supplier Profile — `SCR-121`

**Purpose.** A consolidated, read-first view of the company's identity, offerings, and compliance,
with clear entry points to edit each section (edits may re-trigger review).

**Persona(s).** `supplier_admin` (full), `supplier_user` (read + permitted sections).

**Entry points.** Dashboard profile-health card; `AppShell` nav; approval notification.

**Layout (regions).**
- `block-start`: `PageHeader` — company name, `StatusBadge`, `ExternalId`/sync chip (if ERP-synced),
  completeness `ProgressMeter`.
- **Tabbed body** (`Tabs`): Overview · Company & legal · Contacts & representatives · Addresses &
  branches · Banking · Categories & offerings · Documents. Each tab is a summary `Card` with an
  "Edit" affordance routing to the matching editor (`SCR-122`–`SCR-127`, `SCR-130`).
- `inline-end` rail (desktop): review status / last decision, quick "what buyers see" preview.

**Components used.** `PageHeader`, `Tabs`, `Card`, `DescriptionList`, `StatusBadge`, `ProgressMeter`,
`Money` (bank masked), `Timeline` (review history).

**Data shown.** Legal info (VO), contacts/representatives, addresses/branches, bank accounts (masked),
category links + offering catalog, document summary, onboarding/lifecycle state, `SyncStatus` +
`LastSyncedAt`.

**Actions / permissions.** Edit sections (`supplier.profile.edit`), manage banking
(`supplier.banking.edit`), manage offerings (`supplier.offerings.edit`). Sensitive fields (bank)
masked and require the specific permission to reveal/edit.

**Validation.** N/A on view; each editor owns its validation.

**States.**
- *Loading:* skeleton header + tab panels.
- *Empty section:* per-tab `EmptyState` with "Add" CTA (e.g., no branches yet).
- *Under review:* edit affordances that would re-open review show a caution `Tooltip`.
- *Error:* section-scoped `ErrorPanel` + retry.
- *ERP-degraded:* sync chip shows "sync paused" without blocking edits.

**Mobile behavior.** `Tabs` collapse into a select/accordion; rail content moves below overview;
edit opens full-screen.

**RTL notes.** `DescriptionList` label/value pairs align to inline-start; masked bank numbers use
`bdi`; sync/clock icons mirror.

---

## 4. Documents Center — `SCR-130`

**Purpose.** The supplier's single place to see and manage every document across its lifecycle,
including expiry and review outcomes.

**Persona(s).** `supplier_admin` (manage), `supplier_user` (view + permitted upload).

**Entry points.** Dashboard document-health card; onboarding "Documents"; expiry notifications;
info-requested (`SCR-109`).

**Layout (regions).**
- `block-start`: `PageHeader` + filter bar (status, type, expiry window) + "Upload document" primary.
- **Attention band** (conditional `Banner`): counts of Rejected / Expired / ExpiringSoon → filters.
- **Body**: `DataTable` — columns Document type · Filename · Status (`StatusBadge`) · Uploaded ·
  Expiry (date + relative) · Actions (view `SCR-132`, replace `SCR-131`, download).

**Components used.** `PageHeader`, `FilterBar`, `Banner`, `DataTable`, `StatusBadge`, `Menu`
(row actions), `Dialog` (`SCR-131`), `EmptyState`, `SkeletonList`.

**Data shown.** All `SupplierDocument`s with type, status (Required/Uploaded/UnderReview/Approved/
Rejected + ExpiringSoon/Expired), version count, expiry, rejection reason.

**Actions / permissions.** Upload/replace (`document.upload`), download (`document.read`), view
history (`document.read`). Delete not offered to suppliers (audit + lifecycle governed).

**Validation.** Upload dialog validates type/size (see `SCR-106`).

**States.**
- *Loading:* `SkeletonList` rows.
- *Empty:* `EmptyState` "No documents yet" + upload CTA + list of required types.
- *Filtered-empty:* "No documents match these filters" + reset.
- *Error:* `ErrorPanel` + retry; table preserves filters.

**Mobile behavior.** Table → stacked `Card` rows (type + status prominent, expiry secondary); filters
in a `Drawer`; upload as bottom sheet.

**RTL notes.** Dates render locale-aware with `bdi`-isolated digits; expiry "in 12 days" phrasing
localized; status column stays inline-start-aligned.

---

## 5. RFQ List — Invitations (Supplier) — `SCR-140`

**Purpose.** Let the supplier find, prioritize, and act on the RFQs they are invited to, ordered by
urgency (submission deadline).

**Persona(s).** `supplier_admin`, `supplier_user`.

**Entry points.** Dashboard invitations widget; invitation notification/email; bottom nav (mobile).

**Layout (regions).**
- `block-start`: `PageHeader` + tabs/segments (Open · Closing soon · Responded · Closed/Awarded) +
  filter bar (category, deadline, invitation status) + search.
- **Body**: `DataTable` — RFQ code · Title · Buyer org · Category · Submission deadline (countdown) ·
  Invitation status (Invited/Viewed/Responding/Submitted/Declined) · Proposal state · Action.

**Components used.** `PageHeader`, `Segmented`, `FilterBar`, `DataTable`, `StatusBadge`,
`CountdownPill`, `EmptyState`, `SkeletonList`, `Menu`.

**Data shown.** RFQs where the caller's supplier has an `Invitation`; RFQ `RfqState`
(Published/SubmissionOpen/SubmissionClosed…), deadline, whether a Proposal exists and its state.

**Actions / permissions.** Open RFQ (`rfq.read`), start/continue proposal (`proposal.create`/
`proposal.edit`), decline (`rfq.invitation.decline`). Actions gated by RFQ state (can't start a
proposal once SubmissionClosed).

**Validation.** N/A (navigation surface).

**States.**
- *Loading:* skeleton table.
- *Empty:* `EmptyState` "No invitations yet" + guidance to complete profile/offerings so buyers
  discover you.
- *Filtered-empty:* reset affordance.
- *Error:* `ErrorPanel` + retry.
- *Closing-soon emphasis:* rows within the deadline window show a warning `CountdownPill`.

**Mobile behavior.** Rows become `Card`s (title + countdown primary); segments become a scrollable
chip row; filters in `Drawer`.

**RTL notes.** Countdown pills keep numerals LTR-internal; deadline column inline-start; "closing in"
copy localized; org names may mix scripts (use `bdi`).

---

## 6. RFQ Detail (Supplier) — `SCR-141`

**Purpose.** Give the supplier everything needed to decide and respond: scope, line items,
requirements, attachments, timeline, clarifications, and a clear path to build a proposal.

**Persona(s).** `supplier_admin`, `supplier_user`.

**Entry points.** `SCR-140` row; invitation notification; clarification-answered notification.

**Layout (regions).**
- `block-start`: `PageHeader` — RFQ code + title, buyer org, `StatusBadge`, submission
  `CountdownPill`; primary action **Build proposal** (or Continue draft / View submitted).
- **Timeline strip**: published → submission open → close → evaluation (`Timeline`, current step
  emphasized).
- **Tabbed body** (`Tabs`): Overview (scope, terms, currency) · Items (`DataTable`) · Requirements
  (with mandatory documents) · Attachments (`SCR-142`) · Clarifications (`SCR-143`).
- `inline-end` rail (desktop): key facts (deadline, currency, incoterm, category), proposal status
  card, decline affordance (`SCR-144`).

**Components used.** `PageHeader`, `Timeline`, `Tabs`, `DataTable`, `StatusBadge`, `CountdownPill`,
`Money`, `FileList`, `Card`, `Button`.

**Data shown.** RFQ scope/description, `RfqItem[]` (qty, UoM, spec), `Requirement[]`, `Attachment[]`,
timeline, currency/incoterm, clarifications (published answers), the supplier's proposal state.

**Actions / permissions.** Build/continue proposal (`proposal.create`/`proposal.edit`, only while
SubmissionOpen), ask clarification (`rfq.clarification.ask`), decline invitation
(`rfq.invitation.decline`), download attachments (`rfq.read`).

**Validation.** N/A on view; actions validate on target screens.

**States.**
- *Loading:* skeleton header + tabs.
- *SubmissionClosed:* "Build proposal" disabled with reason `Tooltip`; read-only.
- *Denied:* if the invitation was withdrawn/expired → `SCR-041` affordance.
- *Empty tabs:* e.g., no clarifications yet → `EmptyState`.
- *Error:* `ErrorPanel` + retry.

**Mobile behavior.** Rail facts move into a collapsible summary under the header; sticky primary
action bar at `block-end`; tabs scrollable.

**RTL notes.** Timeline flows inline-start→inline-end and mirrors under RTL; item quantities/`Money`
LTR-internal; incoterm codes (Latin) isolated with `bdi`.

---

## 7. Proposal Builder — `SCR-151`

**Purpose.** Let the supplier author a complete, valid proposal with draft safety and clear
guardrails, then hand off to review & submit (`SCR-152`).

**Persona(s).** `supplier_admin`, `supplier_user`.

**Entry points.** `SCR-141` "Build proposal"; `SCR-150` continue draft; revise flow (`SCR-155`).

**Layout (regions).**
- `block-start`: `PageHeader` — RFQ code/title, submission `CountdownPill`, **autosave/draft status**
  indicator ("Saved just now"), Save draft · Review & submit.
- **Section navigator** (`inline-start` on desktop): Line pricing · Commercial terms · Technical
  response · Documents · Validity — with per-section completeness ticks.
- **Body** (per section):
  - *Line pricing:* editable `DataTable` mirroring `RfqItem[]` — unit price (`Money`), qty (locked),
    line total (computed), currency; running grand total pinned.
  - *Commercial terms:* payment terms, delivery/lead time, incoterm, warranty (generic fields).
  - *Technical response:* structured text/attachments per requirement.
  - *Documents:* `FileDropzone` for proposal documents.
  - *Validity:* offer validity date.

**Components used.** `PageHeader`, `SectionNav`, `DataTable` (editable), `Money`, `FormField`,
`FileDropzone`, `AutosaveIndicator`, `Toast`, `WizardFooter`, `Dialog` (leave-with-unsaved guard).

**Data shown.** RFQ items/requirements/currency; existing draft `Proposal` (ProposalItem[] pricing,
CommercialTerms VO, TechnicalResponse, ProposalDocument[], Validity, `ProposalState`).

**Actions / permissions.** Edit/save draft (`proposal.edit`), attach docs (`proposal.document.upload`),
proceed to submit (`proposal.submit` on `SCR-152`). Editing blocked once SubmissionClosed or state
past Submitted (except during ClarificationRequested → revise).

**Validation.** Client (Zod): required prices ≥ 0, currency selected, validity ≥ deadline, mandatory
requirement responses/documents present. Server (FluentValidation) re-validates; `RowVersion`
concurrency guarded (multiple team editors → 409 reconcile prompt). Autosave never submits.

**States.**
- *Loading:* skeleton sections.
- *Draft/autosave:* continuous save; offline queueing with retry (non-blocking) — ERP independent.
- *Validation:* per-section error badges; Review & submit disabled until required sections pass.
- *Concurrency conflict:* `Dialog` "This proposal changed in another tab/user" → reload/merge.
- *Deadline passed mid-edit:* lock editor, explain, offer read-only.
- *Error:* inline save error + retry; input preserved.

**Mobile behavior.** Section nav becomes a top scrollable chip row / accordion; pricing table becomes
stacked line cards (item + unit price + line total); grand total sticky at `block-end`; leave-guard on
back gesture.

**RTL notes.** Pricing table numeric columns are LTR-internal (tabular figures) within an RTL table;
grand total aligns inline-end; currency symbol placement locale-aware; autosave text localized.

---

## 8. Proposal Comparison Matrix — `SCR-432`

**Purpose.** Give procurement a decision-grade, side-by-side view of all submitted proposals for an
RFQ — pricing, terms, and (once available) evaluation scores — to support shortlisting and
recommendation.

**Persona(s).** `procurement_officer`, `procurement_manager`.

**Entry points.** `SCR-420` RFQ workspace → Compare; `SCR-430` received proposals; `SCR-436` results.

**Layout (regions).**
- `block-start`: `PageHeader` — RFQ code/title, submission status, controls (columns = suppliers;
  toggle: normalize currency, show/hide scores, pin lowest/highest).
- **Matrix** (`inline-start` frozen row headers = criteria/line items; columns = proposals):
  - Group 1 *Commercial:* per-line unit price + line totals, grand total, currency, payment/lead terms.
    Best value per row highlighted (`accent-gold` marker + text label, never color alone).
  - Group 2 *Requirements:* met/partial/not-met per requirement.
  - Group 3 *Evaluation:* per-criterion consolidated scores + weighted total + rank (when
    `Consolidated`).
- `block-end`: summary row (grand total, rank, shortlist checkbox) + bulk "Add to shortlist".

**Components used.** `ComparisonMatrix` (frozen headers, horizontal scroll), `Money`, `StatusBadge`,
`ScoreBar`, `Checkbox`, `Tooltip`, `SegmentedToggle`, `EmptyState`, `SkeletonTable`, `ErrorPanel`.

**Data shown.** All submitted `Proposal`s for the RFQ; ProposalItem pricing, CommercialTerms,
requirement fulfilment; `Evaluation` ConsolidatedResult scores/ranks when finalized. Row-scoped to the
buyer's `OrganizationId`.

**Actions / permissions.** View (`proposal.read` + `rfq.read`), shortlist (`rfq.shortlist`), open a
proposal (`SCR-431`), request clarification (`SCR-433`). Award actions live in `SCR-437`–`SCR-439`.

**Validation.** Shortlist requires ≥1 selection before "Recommendation" is enabled; normalize-currency
requires an FX/display-currency choice `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` for cross-
currency comparison.

**States.**
- *Loading:* skeleton matrix.
- *Empty:* no proposals submitted → `EmptyState` (with SubmissionOpen countdown or "none received").
- *Scores pending:* evaluation group shows "Awaiting consolidation" placeholder rows.
- *Single proposal:* matrix still renders (comparison-of-one) with note.
- *Error:* `ErrorPanel` + retry, controls preserved.

**Mobile behavior.** Comparison is desktop-optimized; on small screens it degrades to a
one-supplier-at-a-time card view with a supplier switcher and a "differences only" filter; horizontal
scroll retained on tablet.

**RTL notes.** Frozen headers sit on inline-start and mirror; the matrix scrolls inline-end; numeric
cells LTR-internal with tabular figures; "best value" marker mirrors position; rank arrows mirror.

---

## 9. Evaluation Scoring Workspace — `SCR-502`

**Purpose.** Let a committee member score one proposal against the weighted criteria template, with
guidance and notes, before independent submission (blind to peers)
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.

**Persona(s).** `evaluator`.

**Entry points.** `SCR-500` assignments; `SCR-501` brief; `SCR-503` scoring overview.

**Layout (regions).**
- `block-start`: `PageHeader` — RFQ code, proposal reference (supplier identity per anonymity policy),
  progress ("Proposal 2 of 5"), Save · Next proposal.
- **Split body**:
  - `inline-start`: **proposal evidence** (read-only) — technical response, line items, documents,
    requirement fulfilment (scrollable reference).
  - `inline-end`: **scoring panel** — one row per `Criterion` (name, weight, max, threshold), a score
    input (slider/stepper/scale per criterion `scoring type`), optional comment; live weighted subtotal
    and running weighted total; threshold breaches flagged.
- `block-end`: Save draft · mark proposal complete · go to `SCR-503`/`SCR-504` to submit all.

**Components used.** `PageHeader`, `SplitPane`, `ScoreInput` (scale/stepper/slider variants),
`WeightBadge`, `ThresholdFlag`, `TextArea`, `RunningTotal`, `AutosaveIndicator`, `Toast`, `Tooltip`.

**Data shown.** `EvaluationTemplate` `Criterion[]` (name/weight/max/threshold/scoring type); the
proposal's evidence; this evaluator's in-progress `EvaluatorScore[]`. Peers' scores are **not** shown.

**Actions / permissions.** Enter/edit scores (`evaluation.score`, only while `InProgress` and assigned),
save draft, mark complete. Cannot view other evaluators' scores; cannot score after own submission.

**Validation.** Client: score within [0, max] per criterion; all criteria scored before "complete";
comment required when below threshold `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. Server re-checks
assignment + state + range. Submission (`SCR-504`) locks scores.

**States.**
- *Loading:* skeleton split panes.
- *Not assigned / already submitted:* read-only (`SCR-505`) or `SCR-041`.
- *Autosave:* per-criterion save; offline-tolerant queue.
- *Threshold breach:* `ThresholdFlag` + required justification.
- *Error:* inline save error + retry; entered scores preserved.

**Mobile/tablet behavior.** Tablet is a first-class device here: split becomes stacked (evidence
collapsible above scoring); scoring inputs use large touch targets; running total sticky at `block-end`.

**RTL notes.** Scale inputs mirror (low↔high orientation follows reading direction, labeled to avoid
ambiguity); weighted totals LTR-internal; criterion list aligns inline-start.

---

## 10. Procurement Dashboard — `SCR-400`

**Purpose.** Give procurement staff command of their pipeline: what's in each RFQ stage, what's due,
what's waiting on them or on approval, and where suppliers are engaging.

**Persona(s).** `procurement_officer`, `procurement_manager` (manager also sees approval queues).

**Entry points.** Post-login default for procurement personas; `AppShell` home.

**Layout (regions).**
- `block-start`: `PageHeader` — org name/unit, "New RFQ" primary (`SCR-411`), period filter.
- **KPI row** (`StatTile`): Active RFQs · Closing this week · Awaiting my action · Pending approvals ·
  Awards in progress.
- **Pipeline board**: RFQs grouped by `RfqState` (Draft → InternalReview → Published →
  SubmissionOpen → UnderEvaluation → AwardApproval → Awarded) as columns/cards with counts + deadlines.
- Two-column lower body: **Deadlines & tasks** (submissions closing, evaluations due, recommendations
  pending) · **Recent activity / notifications** (`SCR-900`). Manager also gets an **Approvals**
  card → `SCR-401`.

**Components used.** `PageHeader`, `StatTile`, `PipelineBoard`, `DataTable`, `Timeline` (mini),
`StatusBadge`, `Banner` (ERP-degraded), `EmptyState`, `SkeletonList`.

**Data shown.** RFQ counts by state, deadlines, tasks assigned/owned, pending approvals (manager),
award/PO sync status; scoped to `OrganizationId`.

**Actions / permissions.** Create RFQ (`rfq.create`), open RFQ (`rfq.read`), approve (manager:
`rfq.approve`, `award.approve`). Affordances hidden by permission; API authoritative.

**Validation.** N/A (read/nav surface).

**States.**
- *Loading:* skeleton tiles + board.
- *Empty (new org/user):* `EmptyState` "No RFQs yet" + "Create your first RFQ" CTA.
- *Error:* per-widget `ErrorPanel` + retry (isolated failures).
- *ERP-degraded:* `Banner` on award/PO widgets ("PO sync paused"); pipeline unaffected.

**Mobile behavior.** Pipeline board becomes a horizontally scrollable stage strip; KPI tiles 2×2
scroll; tasks list stacked; approval queue in a tab. (Procurement is desktop-optimized but responsive.)

**RTL notes.** Pipeline stages flow inline-start→inline-end and mirror; deadline countdowns and
`Money` LTR-internal; stage progression arrows mirror.

---

## 11. Ministry Governance Dashboard — `SCR-600`

**Purpose.** Give the Ministry a read-only, cross-organization view of ecosystem health: supplier
participation, procurement activity, award outcomes, and cycle-time performance — for oversight, not
operation.

**Persona(s).** `ministry_viewer` (read-only, cross-org aggregate).

**Entry points.** Post-login default for ministry persona; `AppShell` home.

**Layout (regions).**
- `block-start`: `PageHeader` — "Governance overview", period + sector/category filters, export
  (`SCR-605`). A clear **read-only** treatment (no create/edit affordances anywhere).
- **KPI row** (`StatTile`): Registered suppliers (by status) · Active RFQs · Awards (period) ·
  Avg. RFQ cycle time · Supplier participation rate.
- **Charts grid** (Recharts, themed): suppliers by category (bar) · RFQ activity over time (line) ·
  award outcomes / value trend (line/area) · category coverage (treemap/bar). Commercial values are
  shown or anonymized/aggregated per policy `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.
- **Lists**: recent awards (read-only) → `SCR-606`; top categories → `SCR-604`.

**Components used.** `PageHeader`, `StatTile`, Recharts (`BarChart`, `LineChart`, `AreaChart`,
`Treemap`), `DataTable` (read-only), `Money` (or masked/aggregate), `EmptyState`, `SkeletonGrid`,
`ExportMenu`.

**Data shown.** Cross-org aggregates: supplier counts by status/category, RFQ pipeline metrics, award
counts/values (or anonymized), cycle times. No row-level edit; drill-downs are read-only.

**Actions / permissions.** View (`governance.read`), export (`governance.export`). No write
permissions exist for this persona. Any attempt to reach an operational route → `SCR-041`.

**Validation.** N/A (read/export surface); export requests validate period/format.

**States.**
- *Loading:* `SkeletonGrid` for tiles + charts.
- *Empty (no data in period):* per-chart `EmptyState` "No activity for this period".
- *Restricted metric:* where commercial values are policy-hidden, show "Aggregated" / "Restricted"
  labels instead of numbers.
- *Error:* per-widget `ErrorPanel` + retry.

**Mobile behavior.** Charts stack single-column with legends below; filters in a `Drawer`; tables →
cards; export from an overflow menu. (Desktop-optimized.)

**RTL notes.** Chart axes and legends flow RTL; time-series x-axis direction follows locale (right-to-
left progression under `ar`) with clear axis labels; numeric axis values LTR-internal; `Money`/percent
tabular figures.

---

## 12. Admin — Users Management — `SCR-701`

**Purpose.** Let the system administrator provision, find, and govern all users across every surface
(supplier, procurement, evaluation, ministry, admin), assigning roles and scopes and managing status
and MFA.

**Persona(s).** `system_admin` (global scope).

**Entry points.** `SCR-700` admin dashboard; `AppShell` admin nav; audit drill-down.

**Layout (regions).**
- `block-start`: `PageHeader` — "Users", "Invite user" primary (`SCR-703`), search + filter bar
  (role, surface, status, organization/supplier scope).
- **Body**: `DataTable` — Name · Email · Role(s) · Scope (Org/Supplier/Global) · Status
  (Active/Invited/Suspended/Deactivated) · MFA · Last active · Actions (edit `SCR-702`, suspend,
  reset MFA, resend invite).
- **Bulk bar** (on selection): assign role, suspend, deactivate (each writes AuditLog).

**Components used.** `PageHeader`, `FilterBar`, `DataTable`, `StatusBadge`, `Menu` (row actions),
`Dialog` (destructive confirm), `Toast`, `EmptyState`, `SkeletonList`, `ScopeChip`.

**Data shown.** `User` records with `Role`/`Permission` assignments, membership scope
(Organization/Supplier), status, MFA enrollment, last-active. Never displays the user's own email as
an external identifier beyond identification.

**Actions / permissions.** Manage users (`admin.users.manage`): invite, edit role/scope, suspend,
reactivate, deactivate, reset MFA, resend invite. Destructive/irreversible actions require a confirm
`Dialog`; all changes are audited (`SCR-720`). Admin cannot self-lock out (guard on removing own
admin role / last admin).

**Validation.** Invite: valid email, role + scope required, scope must match role's surface. Editing:
prevent removing the last global admin; prevent assigning a supplier scope to a procurement role, etc.
Server (FluentValidation) authoritative; `RowVersion` concurrency on edits.

**States.**
- *Loading:* `SkeletonList`.
- *Empty:* `EmptyState` "No users match" + invite CTA.
- *Filtered-empty:* reset.
- *Error:* `ErrorPanel` + retry; filters/selection preserved.
- *Concurrency conflict:* 409 → reload prompt.
- *Denied:* non-admin reaching this route → `SCR-041` (also affordance-hidden in nav).

**Mobile behavior.** Table → stacked user `Card`s (name + role + status prominent); filters/bulk in a
`Drawer`; row actions in a bottom sheet. (Admin is desktop-optimized but responsive.)

**RTL notes.** Emails and role keys (Latin `resource.action`) isolated with `bdi` inside RTL rows;
status/scope chips align inline-start; last-active relative time localized.

---

## Cross-screen consistency checklist

Every spec above conforms to the canonical decisions:

- **State machines** enforced in the domain, not just the UI — screens disable/hide illegal
  transitions and surface reasons (`00-foundational-decisions.md` §5).
- **RBAC** — affordance-hiding in UI, authoritative check at API; each screen names its
  `resource.action` permissions (§6).
- **ERP boundary** — no core flow blocks on ERP; sync/PO surfaces degrade gracefully (§1).
- **Design tokens & components** — evergreen-teal primary, warm-stone neutrals, gold accent used
  sparingly for awards/KPIs; 4px grid; soft elevation (§7, `DESIGN-SYSTEM.md`).
- **Arabic-first / RTL** — logical properties, mirrored icons, `bdi`-isolated Latin/numeric runs,
  tabular figures for money; Western-Arabic digits default, configurable (§7–§8).
- **A11y** — WCAG 2.2 AA, focus management, color-plus-icon status, reduced-motion, 44px targets (§9).
- **No invented Syrian legal/tax rules** — such fields are generic and tagged
  `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` (§8, Discovery §5).
