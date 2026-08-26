# MOTS Supplier Portal — Screen Inventory

> **Status:** Baseline v1 · **Owner:** UX + Product · **Date:** 2026-08-26
> **Canonical sources:** [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](../product/DISCOVERY-REPORT.md)
> **Related UX docs:** [`SCREEN-SPECIFICATIONS.md`](./SCREEN-SPECIFICATIONS.md) ·
> [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md)

This is the **complete, exhaustive** screen inventory for the portal across **all personas** and
**all states**. It is the master registry that specifications, routes, backlog slices, and E2E tests
reference by **Screen ID (`SCR-###`)**. Every screen implied by the requirements is listed — not only
the "happy-path" pages, but auth, empty, loading, error, permission-denied, and mobile variants.

---

## 0. Conventions

### 0.1 ID ranges (by surface)

| Range | Surface / area |
|---|---|
| `SCR-000`–`SCR-049` | Public, authentication, and global system states (shared by all personas) |
| `SCR-100`–`SCR-199` | Supplier app (`supplier_admin`, `supplier_user`) |
| `SCR-300`–`SCR-349` | Supplier Onboarding / Compliance back-office (`onboarding_reviewer`) |
| `SCR-400`–`SCR-499` | Procurement back-office (`procurement_officer`, `procurement_manager`) |
| `SCR-500`–`SCR-549` | Evaluation committee (`evaluator`) |
| `SCR-600`–`SCR-649` | Ministry governance (`ministry_viewer`) |
| `SCR-700`–`SCR-799` | System administration (`system_admin`) |
| `SCR-900`–`SCR-949` | Cross-cutting shared screens (notifications, account settings, help) |

### 0.2 Persona keys

`supplier_admin`, `supplier_user`, `onboarding_reviewer`, `procurement_officer`,
`procurement_manager`, `evaluator`, `ministry_viewer`, `system_admin`, `public` (unauthenticated).

### 0.3 Priority / phase

Phases align to [`docs/backlog/ROADMAP.md`](../backlog/ROADMAP.md). `P0` = MVP-critical,
`P1` = core, `P2` = enhancement/governance polish.

### 0.4 State vocabulary (the "key states" column)

Every interactive screen is designed for the full state matrix. Abbreviations used below:

| Abbr | State | Design intent |
|---|---|---|
| `auth` | Auth-gated | Redirect to login (`SCR-002`) if session missing/expired |
| `load` | Loading / skeleton | Skeleton rows/cards, never spinner-only for content regions |
| `empty` | Empty (no data yet) | Illustrated empty state + primary CTA / guidance |
| `partial` | Partial / filtered-empty | "No results for filters" with reset affordance |
| `ok` | Populated / success | Normal content; success toasts for actions |
| `err` | Error (server/network) | Inline error panel + retry; preserves user input |
| `valid` | Validation error | Field-level messages, RTL-aware, focus first invalid |
| `denied` | Permission-denied | 403 affordance-hidden + hard block (`SCR-041`) |
| `offline` | ERP-unavailable / degraded | Non-blocking banner; core flow continues (see ERP boundary) |
| `mobile` | Mobile variant | Reflow to single column, bottom nav, sheet dialogs |

> **Global rule:** RBAC is re-checked in the UI **for affordance-hiding only**; the API is the
> authority (`00-foundational-decisions.md` §6). Every `denied` state is backed by a server 403.

---

## 1. Public, Authentication & Global System States (`SCR-00x`–`SCR-04x`)

These are shared by every persona and cover the entire session lifecycle.

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-000 | Public landing / marketing entry | public | `/` | Explain the portal, route to register/login, language + RTL toggle | P0 | load, ok, mobile |
| SCR-001 | Supplier registration (account create) | public | `/register` | Create supplier account (email, password, org legal name, contact) | P0 | ok, valid, err, mobile |
| SCR-002 | Login | all | `/login` | Email + password sign-in; entry to MFA | P0 | ok, valid, err, mobile |
| SCR-003 | MFA challenge | all (MFA-enabled) | `/login/mfa` | Enter TOTP / one-time code (Identity 2FA) | P1 | ok, valid, err, mobile |
| SCR-004 | Email verification pending | public | `/verify/pending` | "Check your inbox" after register; resend link | P0 | ok, err, mobile |
| SCR-005 | Email verification result | public | `/verify/confirm` | Token-consuming landing: verified / invalid / expired token | P0 | load, ok, err, mobile |
| SCR-006 | Forgot password (request) | all | `/password/forgot` | Request reset link by email | P0 | ok, valid, err, mobile |
| SCR-007 | Reset password (set new) | all | `/password/reset` | Token-consuming new-password form + strength meter | P0 | load, ok, valid, err, mobile |
| SCR-008 | Set initial password (invited user) | invited users | `/invite/accept` | Back-office/delegated users set password from invite token | P1 | load, ok, valid, err, mobile |
| SCR-009 | Accept terms & privacy | all first-login | `/onboarding/terms` | Explicit T&C + privacy acceptance gate (versioned) | P0 | ok, valid, err, mobile |
| SCR-010 | Language & locale first-run | all first-login | `/onboarding/locale` | Choose `ar`/`en`, numerals, currency display preference | P1 | ok, mobile |
| SCR-011 | Logout / signed-out confirmation | all | `/logout` | Confirms sign-out; re-login CTA | P0 | ok, mobile |
| SCR-040 | Session expired / re-auth | all | (overlay) | Refresh-token expiry interstitial; preserve destination | P0 | ok, err, mobile |
| SCR-041 | 403 Permission denied | all | `/403` | Hard block for out-of-scope resource; "request access" hint | P0 | ok, mobile |
| SCR-042 | 404 Not found | all | `/404` | Unknown route / opaque slug miss | P0 | ok, mobile |
| SCR-043 | 500 / unexpected error | all | `/error` | App-level error boundary; correlationId shown for support | P0 | ok, mobile |
| SCR-044 | Maintenance / scheduled downtime | all | `/maintenance` | Planned maintenance window notice | P2 | ok, mobile |
| SCR-045 | ERP-degraded global banner | authenticated | (banner) | Non-blocking "some sync features paused" indicator | P1 | offline |
| SCR-046 | Account locked / suspended | affected users | `/account/locked` | Explains lockout (failed attempts) or admin suspension | P1 | ok, mobile |
| SCR-047 | Unsupported browser / low-capability | public | (interstitial) | Graceful degradation notice | P2 | ok, mobile |

---

## 2. Supplier App (`SCR-1xx`) — `supplier_admin`, `supplier_user`

Mobile **and** desktop are first-class here (primary device = mobile + desktop). `supplier_user` is a
delegated representative with a reduced permission set (no user management, no legal/bank edits unless
granted).

### 2.1 Onboarding wizard (multi-step)

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-100 | Onboarding hub / progress overview | supplier_admin | `/onboarding` | Wizard shell: step list, % complete, resume, submit gate | P0 | auth, load, ok, err, mobile |
| SCR-101 | Onboarding · Company & legal info | supplier_admin | `/onboarding/company` | Legal name, registration/tax IDs (generic), type, country | P0 | ok, valid, err, mobile |
| SCR-102 | Onboarding · Contacts & representatives | supplier_admin | `/onboarding/contacts` | Primary + additional contacts/representatives | P0 | ok, valid, empty, err, mobile |
| SCR-103 | Onboarding · Addresses & branches | supplier_admin | `/onboarding/addresses` | HQ address + branch list | P0 | ok, valid, empty, err, mobile |
| SCR-104 | Onboarding · Bank accounts | supplier_admin | `/onboarding/banking` | Bank account(s) for future PO/payment mapping | P1 | ok, valid, empty, err, mobile |
| SCR-105 | Onboarding · Categories & offerings | supplier_admin | `/onboarding/offerings` | Select category tree nodes; declare offerings | P0 | ok, valid, empty, err, mobile |
| SCR-106 | Onboarding · Documents upload | supplier_admin | `/onboarding/documents` | Upload required document types; per-doc status | P0 | ok, load, valid, err, empty, mobile |
| SCR-107 | Onboarding · Review & submit | supplier_admin | `/onboarding/review` | Read-only summary, completeness check, submit | P0 | ok, valid, err, mobile |
| SCR-108 | Onboarding · Submitted confirmation | supplier_admin | `/onboarding/submitted` | "Under review" confirmation + expected next steps | P0 | ok, mobile |
| SCR-109 | Onboarding · Info requested (resubmit) | supplier_admin, supplier_user | `/onboarding/info-requested` | Reviewer's requested changes; targeted resubmission | P0 | ok, valid, err, empty, mobile |
| SCR-110 | Onboarding · Rejected outcome | supplier_admin | `/onboarding/rejected` | Rejection reason + guidance / appeal contact | P1 | ok, mobile |

### 2.2 Supplier home & profile

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-120 | Supplier dashboard | supplier_admin, supplier_user | `/dashboard` | Home: onboarding/profile health, invitations, proposals, alerts | P0 | auth, load, empty, ok, err, offline, mobile |
| SCR-121 | Supplier profile (view) | supplier_admin, supplier_user | `/profile` | Consolidated company profile, completeness, status badges | P0 | load, ok, err, mobile |
| SCR-122 | Profile · Edit company & legal | supplier_admin | `/profile/company` | Edit legal info (re-review may trigger) | P1 | ok, valid, err, denied, mobile |
| SCR-123 | Profile · Contacts & representatives | supplier_admin | `/profile/contacts` | CRUD contacts/representatives | P1 | load, ok, empty, valid, err, denied, mobile |
| SCR-124 | Profile · Addresses & branches | supplier_admin | `/profile/branches` | CRUD addresses/branches | P1 | load, ok, empty, valid, err, denied, mobile |
| SCR-125 | Profile · Bank accounts | supplier_admin | `/profile/banking` | CRUD bank accounts (sensitive; masked) | P1 | load, ok, empty, valid, err, denied, mobile |
| SCR-126 | Profile · Categories & offerings | supplier_admin | `/profile/offerings` | Manage category links + offering catalog | P1 | load, ok, empty, valid, err, mobile |
| SCR-127 | Offering detail / editor | supplier_admin | `/profile/offerings/:slug` | Single offering: specs, UoM, attributes (JSONB-backed) | P1 | load, ok, valid, err, denied, mobile |

### 2.3 Documents

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-130 | Documents center | supplier_admin, supplier_user | `/documents` | All documents: type, status, expiry, actions | P0 | auth, load, empty, ok, err, mobile |
| SCR-131 | Document upload / replace dialog | supplier_admin | `/documents/upload` | Upload/replace a document for a type | P0 | ok, load, valid, err, mobile |
| SCR-132 | Document detail / history | supplier_admin, supplier_user | `/documents/:slug` | Versions, review outcome, rejection reason, expiry timeline | P1 | load, ok, err, denied, mobile |
| SCR-133 | Document expiring/expired alerts | supplier_admin | `/documents/attention` | Filtered view: ExpiringSoon + Expired + Rejected | P1 | load, empty, ok, mobile |

### 2.4 RFQs, invitations & clarifications (supplier side)

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-140 | Invitations / RFQ list (supplier) | supplier_admin, supplier_user | `/rfqs` | RFQs the supplier is invited to; status, deadlines, filters | P0 | auth, load, empty, partial, ok, err, mobile |
| SCR-141 | RFQ detail (supplier) | supplier_admin, supplier_user | `/rfqs/:code` | Scope, items, requirements, attachments, timeline, Q&A entry | P0 | load, ok, err, denied, mobile |
| SCR-142 | RFQ attachments viewer | supplier_admin, supplier_user | `/rfqs/:code/attachments` | Download/preview RFQ documents | P1 | load, empty, ok, err, mobile |
| SCR-143 | Clarifications (supplier Q&A) | supplier_admin, supplier_user | `/rfqs/:code/clarifications` | Ask questions; view published answers | P1 | load, empty, ok, valid, err, mobile |
| SCR-144 | Decline invitation dialog | supplier_admin, supplier_user | `/rfqs/:code/decline` | Decline to bid with optional reason | P1 | ok, valid, err, mobile |

### 2.5 Proposals (supplier side)

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-150 | Proposals list | supplier_admin, supplier_user | `/proposals` | All proposals across RFQs; state, validity, deadlines | P0 | auth, load, empty, partial, ok, err, mobile |
| SCR-151 | Proposal builder | supplier_admin, supplier_user | `/rfqs/:code/proposal` | Author draft: line pricing, terms, technical response, docs | P0 | load, ok, valid, err, denied, offline, mobile |
| SCR-152 | Proposal · Review & submit | supplier_admin, supplier_user | `/rfqs/:code/proposal/review` | Pre-submission summary, guardrails, submit | P0 | ok, valid, err, mobile |
| SCR-153 | Proposal submitted confirmation | supplier_admin, supplier_user | `/rfqs/:code/proposal/submitted` | Receipt + validity + next steps | P0 | ok, mobile |
| SCR-154 | Proposal detail (read-only) | supplier_admin, supplier_user | `/proposals/:code` | Submitted proposal, status timeline | P1 | load, ok, err, denied, mobile |
| SCR-155 | Clarification requested → revise | supplier_admin, supplier_user | `/proposals/:code/revise` | Respond to buyer clarification; submit revision | P1 | load, ok, valid, err, mobile |
| SCR-156 | Withdraw proposal dialog | supplier_admin, supplier_user | `/proposals/:code/withdraw` | Withdraw while SubmissionOpen | P1 | ok, valid, err, mobile |
| SCR-157 | Award offer / outcome (supplier) | supplier_admin | `/proposals/:code/award` | AwardOffered → accept/decline; outcome notice | P1 | load, ok, valid, err, mobile |

### 2.6 Supplier team & account

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-160 | Supplier team / users | supplier_admin | `/team` | Invite/manage delegated `supplier_user`s; roles/scopes | P1 | load, empty, ok, valid, err, denied, mobile |
| SCR-161 | Invite team member dialog | supplier_admin | `/team/invite` | Send invite; assign permissions | P1 | ok, valid, err, mobile |
| SCR-162 | Team member detail / edit | supplier_admin | `/team/:slug` | Edit permissions, deactivate | P1 | load, ok, valid, err, denied, mobile |

---

## 3. Supplier Onboarding / Compliance Back-office (`SCR-3xx`) — `onboarding_reviewer`

Desktop-first.

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-300 | Onboarding review dashboard | onboarding_reviewer | `/review` | KPIs: pending, info-requested, SLA aging, workload | P0 | auth, load, empty, ok, err, mobile |
| SCR-301 | Onboarding review queue | onboarding_reviewer | `/review/queue` | Filter/sort submitted suppliers; claim/assign | P0 | load, empty, partial, ok, err, mobile |
| SCR-302 | Supplier review workspace | onboarding_reviewer | `/review/:code` | Full submission review; approve/reject/request-info | P0 | load, ok, err, denied, mobile |
| SCR-303 | Document review panel | onboarding_reviewer | `/review/:code/documents` | Per-document approve/reject with reason; expiry set | P0 | load, empty, ok, valid, err, mobile |
| SCR-304 | Request information composer | onboarding_reviewer | `/review/:code/request-info` | Structured change requests back to supplier | P0 | ok, valid, err, mobile |
| SCR-305 | Approve supplier dialog | onboarding_reviewer | `/review/:code/approve` | Final approval → Active; triggers ERP sync outbox | P0 | ok, valid, err, offline, mobile |
| SCR-306 | Reject supplier dialog | onboarding_reviewer | `/review/:code/reject` | Rejection with mandatory reason | P0 | ok, valid, err, mobile |
| SCR-307 | Supplier directory (compliance view) | onboarding_reviewer | `/review/suppliers` | All suppliers, onboarding + document health | P1 | load, empty, partial, ok, err, mobile |
| SCR-308 | Supplier lifecycle actions | onboarding_reviewer | `/review/:code/lifecycle` | Suspend / reactivate / deactivate with audit | P1 | ok, valid, err, denied, mobile |
| SCR-309 | Document expiry monitor | onboarding_reviewer | `/review/expiries` | Cross-supplier expiring/expired documents watchlist | P1 | load, empty, ok, err, mobile |

---

## 4. Procurement Back-office (`SCR-4xx`) — `procurement_officer`, `procurement_manager`

Desktop-first. `procurement_manager` adds approval authority (RFQ publication, award).

### 4.1 Dashboards & directory

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-400 | Procurement dashboard | procurement_officer, procurement_manager | `/procurement` | Pipeline by RFQ state, deadlines, approvals, alerts | P0 | auth, load, empty, ok, err, offline, mobile |
| SCR-401 | Manager approval dashboard | procurement_manager | `/procurement/approvals` | Queues: RFQ publish approvals + award approvals | P0 | load, empty, ok, err, denied, mobile |
| SCR-402 | Supplier directory (procurement) | procurement_officer, procurement_manager | `/procurement/suppliers` | Browse/search approved suppliers by category for invites | P1 | load, empty, partial, ok, err, mobile |

### 4.2 RFQ authoring & lifecycle

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-410 | RFQ list (procurement) | procurement_officer, procurement_manager | `/procurement/rfqs` | All org RFQs; state, timeline, owner, filters | P0 | auth, load, empty, partial, ok, err, mobile |
| SCR-411 | RFQ create/edit — Basics | procurement_officer | `/procurement/rfqs/new` | Title, scope, category, currency, timeline | P0 | ok, valid, err, mobile |
| SCR-412 | RFQ edit — Items | procurement_officer | `/procurement/rfqs/:code/items` | Line items, UoM, quantities, specs | P0 | load, ok, empty, valid, err, mobile |
| SCR-413 | RFQ edit — Requirements | procurement_officer | `/procurement/rfqs/:code/requirements` | Technical/commercial requirements, mandatory docs | P0 | load, ok, empty, valid, err, mobile |
| SCR-414 | RFQ edit — Attachments | procurement_officer | `/procurement/rfqs/:code/attachments` | Upload RFQ documents | P1 | load, empty, ok, valid, err, mobile |
| SCR-415 | RFQ edit — Evaluation template | procurement_officer | `/procurement/rfqs/:code/evaluation` | Bind weighted criteria template; thresholds | P0 | load, ok, valid, err, mobile |
| SCR-416 | RFQ edit — Invitations | procurement_officer | `/procurement/rfqs/:code/invitations` | Select/invite suppliers; track invite status | P0 | load, empty, ok, valid, err, mobile |
| SCR-417 | RFQ — Submit for internal review | procurement_officer | `/procurement/rfqs/:code/submit-review` | Move Draft → InternalReview | P0 | ok, valid, err, mobile |
| SCR-418 | RFQ — Internal review / approve | procurement_manager | `/procurement/rfqs/:code/approve` | Approve/return RFQ before publish | P0 | load, ok, valid, err, denied, mobile |
| SCR-419 | RFQ — Publish dialog | procurement_officer, procurement_manager | `/procurement/rfqs/:code/publish` | Approved → Published → SubmissionOpen | P0 | ok, valid, err, mobile |
| SCR-420 | RFQ detail (procurement) | procurement_officer, procurement_manager | `/procurement/rfqs/:code` | Full RFQ workspace; state actions; tabs | P0 | load, ok, err, denied, offline, mobile |
| SCR-421 | RFQ clarifications management | procurement_officer | `/procurement/rfqs/:code/clarifications` | Triage supplier questions; publish answers | P1 | load, empty, ok, valid, err, mobile |
| SCR-422 | RFQ cancel dialog | procurement_officer, procurement_manager | `/procurement/rfqs/:code/cancel` | Cancel pre-Awarded with reason + audit | P1 | ok, valid, err, denied, mobile |
| SCR-423 | RFQ close submissions dialog | procurement_officer | `/procurement/rfqs/:code/close` | SubmissionOpen → SubmissionClosed | P0 | ok, valid, err, mobile |

### 4.3 Proposals, comparison, evaluation setup & award

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-430 | Received proposals list | procurement_officer, procurement_manager | `/procurement/rfqs/:code/proposals` | All submissions for an RFQ; state, totals | P0 | load, empty, partial, ok, err, mobile |
| SCR-431 | Proposal detail (buyer view) | procurement_officer, procurement_manager, evaluator | `/procurement/rfqs/:code/proposals/:slug` | Single proposal deep view | P0 | load, ok, err, denied, mobile |
| SCR-432 | Proposal comparison matrix | procurement_officer, procurement_manager | `/procurement/rfqs/:code/compare` | Side-by-side line/term/score comparison | P0 | load, empty, ok, err, mobile |
| SCR-433 | Request proposal clarification | procurement_officer | `/procurement/rfqs/:code/proposals/:slug/clarify` | Ask supplier to revise/clarify | P1 | ok, valid, err, mobile |
| SCR-434 | Evaluation setup / committee | procurement_officer | `/procurement/rfqs/:code/evaluation/setup` | Assign evaluators; confirm template; open scoring | P0 | load, ok, empty, valid, err, mobile |
| SCR-435 | Evaluation progress monitor | procurement_officer, procurement_manager | `/procurement/rfqs/:code/evaluation` | Per-evaluator progress; consolidate | P0 | load, empty, ok, err, mobile |
| SCR-436 | Consolidated results & shortlist | procurement_officer, procurement_manager | `/procurement/rfqs/:code/results` | Ranked results; shortlist selection | P0 | load, empty, ok, valid, err, mobile |
| SCR-437 | Recommendation composer | procurement_officer | `/procurement/rfqs/:code/recommendation` | Draft award recommendation + justification | P0 | load, ok, valid, err, mobile |
| SCR-438 | Award approval workspace | procurement_manager | `/procurement/rfqs/:code/award/approve` | Approve/reject recommendation | P0 | load, ok, valid, err, denied, mobile |
| SCR-439 | Award decision & notify | procurement_officer, procurement_manager | `/procurement/rfqs/:code/award/decide` | Finalize award; emit ERP-PO outbox; notify | P0 | ok, valid, err, offline, mobile |
| SCR-440 | Award outcome / PO sync status | procurement_officer, procurement_manager | `/procurement/rfqs/:code/award` | Award record, ERP PO ref/sync state | P1 | load, ok, err, offline, mobile |

---

## 5. Evaluation Committee (`SCR-5xx`) — `evaluator`

Desktop / tablet. Evaluators score **independently and blind** to peers before consolidation
`[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-500 | Evaluator dashboard / assignments | evaluator | `/evaluation` | RFQs assigned to me; due dates; progress | P0 | auth, load, empty, ok, err, mobile |
| SCR-501 | Evaluation instructions / brief | evaluator | `/evaluation/:code/brief` | Criteria, weights, thresholds, scoring rules | P0 | load, ok, err, mobile |
| SCR-502 | Proposal scoring workspace | evaluator | `/evaluation/:code/proposals/:slug/score` | Score one proposal against criteria; notes | P0 | load, ok, valid, err, denied, mobile |
| SCR-503 | Scoring overview (my scores) | evaluator | `/evaluation/:code/scores` | All my proposal scores; edit before submit | P0 | load, empty, ok, valid, err, mobile |
| SCR-504 | Submit evaluation dialog | evaluator | `/evaluation/:code/submit` | Lock and submit my independent scores | P0 | ok, valid, err, mobile |
| SCR-505 | Submitted / read-only scores | evaluator | `/evaluation/:code/submitted` | Post-submit read-only; consolidation pending | P1 | load, ok, err, mobile |

---

## 6. Ministry Governance (`SCR-6xx`) — `ministry_viewer`

**Read-only**, cross-organization, aggregate access. Whether commercial values are visible vs.
anonymized/aggregate is `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` — screens are built to switch.

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-600 | Ministry governance dashboard | ministry_viewer | `/ministry` | Ecosystem KPIs: suppliers, RFQs, awards, cycle times | P1 | auth, load, empty, ok, err, mobile |
| SCR-601 | Supplier registry (governance) | ministry_viewer | `/ministry/suppliers` | Cross-org supplier population, categories, status | P1 | load, empty, partial, ok, err, mobile |
| SCR-602 | RFQ activity monitor | ministry_viewer | `/ministry/rfqs` | Cross-org RFQ pipeline & outcomes (read-only) | P1 | load, empty, partial, ok, err, mobile |
| SCR-603 | Award / spend analytics | ministry_viewer | `/ministry/awards` | Award trends; spend (or anonymized) analytics | P2 | load, empty, ok, err, mobile |
| SCR-604 | Category & sector analytics | ministry_viewer | `/ministry/categories` | Participation/coverage by category tree | P2 | load, empty, ok, err, mobile |
| SCR-605 | Governance reports / export | ministry_viewer | `/ministry/reports` | Generate/export governance reports | P2 | load, empty, ok, err, mobile |
| SCR-606 | Read-only entity drill-down | ministry_viewer | `/ministry/rfqs/:code` | Non-editable RFQ/award detail | P2 | load, ok, denied, err, mobile |

---

## 7. System Administration (`SCR-7xx`) — `system_admin`

Desktop-first, global scope.

### 7.1 Identity, roles & organizations

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-700 | Admin dashboard | system_admin | `/admin` | System health, users, jobs, sync, audit summary | P1 | auth, load, ok, err, offline, mobile |
| SCR-701 | Users management | system_admin | `/admin/users` | List/search users across surfaces; status | P0 | load, empty, partial, ok, err, mobile |
| SCR-702 | User detail / edit | system_admin | `/admin/users/:slug` | Roles, scope, activation, MFA reset | P0 | load, ok, valid, err, denied, mobile |
| SCR-703 | Create / invite user | system_admin | `/admin/users/new` | Provision back-office/admin user; assign role+scope | P0 | ok, valid, err, mobile |
| SCR-704 | Roles & permissions | system_admin | `/admin/roles` | Manage roles; edit permission sets | P1 | load, ok, valid, err, mobile |
| SCR-705 | Role editor (permission matrix) | system_admin | `/admin/roles/:slug` | `resource.action` matrix editor | P1 | load, ok, valid, err, mobile |
| SCR-706 | Organizations management | system_admin | `/admin/organizations` | Buying entities / hotels / MOT bodies | P1 | load, empty, ok, valid, err, mobile |
| SCR-707 | Organization detail / org units | system_admin | `/admin/organizations/:slug` | Org units, members, scope config | P1 | load, ok, valid, err, mobile |

### 7.2 Reference data & templates

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-710 | Category tree manager | system_admin | `/admin/categories` | Manage hierarchical category taxonomy | P1 | load, empty, ok, valid, err, mobile |
| SCR-711 | Document types manager | system_admin | `/admin/document-types` | Types, requiredness, expiry rules (generic) | P1 | load, empty, ok, valid, err, mobile |
| SCR-712 | Reference data (currencies/UoM/incoterms/regions) | system_admin | `/admin/reference` | Manage currency, UoM, incoterm, region lists | P1 | load, empty, ok, valid, err, mobile |
| SCR-713 | Evaluation templates manager | system_admin | `/admin/evaluation-templates` | Create/edit weighted-criteria templates | P0 | load, empty, ok, valid, err, mobile |
| SCR-714 | Evaluation template editor | system_admin | `/admin/evaluation-templates/:slug` | Criteria, weights, max, thresholds, scoring type | P0 | load, ok, valid, err, mobile |
| SCR-715 | Notification templates | system_admin | `/admin/notifications/templates` | Email/in-app template content (ar/en) | P1 | load, empty, ok, valid, err, mobile |
| SCR-716 | Localization / string overrides | system_admin | `/admin/localization` | Manage locale strings, numerals, currency display | P2 | load, ok, valid, err, mobile |

### 7.3 Operations, integration & audit

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-720 | Audit log explorer | system_admin, onboarding_reviewer | `/admin/audit` | Search state-change audit (actor, from→to, correlationId) | P0 | load, empty, partial, ok, err, mobile |
| SCR-721 | Background jobs (Hangfire) monitor | system_admin | `/admin/jobs` | Recurring/scheduled jobs, retries, dead-letter | P1 | load, empty, ok, err, mobile |
| SCR-722 | Outbox / integration events | system_admin | `/admin/outbox` | Pending/failed outbox messages; replay | P1 | load, empty, ok, err, offline, mobile |
| SCR-723 | ERP sync monitor | system_admin | `/admin/erp-sync` | Sync status per entity; ExternalId mapping; retry | P1 | load, empty, ok, err, offline, mobile |
| SCR-724 | System settings | system_admin | `/admin/settings` | Global config: defaults, feature flags, storage | P1 | load, ok, valid, err, mobile |
| SCR-725 | Storage / file settings | system_admin | `/admin/settings/storage` | `IFileStorage` provider config (local/S3-compatible) | P2 | load, ok, valid, err, mobile |
| SCR-726 | Security settings | system_admin | `/admin/settings/security` | Password policy, MFA enforcement, lockout | P1 | load, ok, valid, err, mobile |

---

## 8. Cross-cutting Shared Screens (`SCR-9xx`)

Available (scoped) to multiple/all authenticated personas via the app shell.

| ID | Screen | Persona(s) | Route | Purpose | Phase | Key states |
|---|---|---|---|---|---|---|
| SCR-900 | Notifications center | all authenticated | `/notifications` | In-app notification inbox; filters; mark read | P0 | auth, load, empty, ok, err, mobile |
| SCR-901 | Notification preferences | all authenticated | `/settings/notifications` | Channel/opt-in per event type | P1 | load, ok, valid, err, mobile |
| SCR-902 | Account settings / profile | all authenticated | `/settings/account` | Name, language, numerals, contact | P0 | load, ok, valid, err, mobile |
| SCR-903 | Security & password | all authenticated | `/settings/security` | Change password, manage MFA, active sessions | P0 | load, ok, valid, err, mobile |
| SCR-904 | Manage MFA / authenticator | all authenticated | `/settings/security/mfa` | Enroll/reset TOTP, recovery codes | P1 | load, ok, valid, err, mobile |
| SCR-905 | Active sessions / devices | all authenticated | `/settings/security/sessions` | View/revoke refresh-token sessions | P2 | load, empty, ok, err, mobile |
| SCR-906 | Global search results | back-office personas | `/search` | Cross-entity scoped search results | P2 | load, empty, partial, ok, err, mobile |
| SCR-907 | Help / support center | all authenticated | `/help` | FAQ, contextual guidance, contact support | P2 | load, ok, mobile |
| SCR-908 | About / version / legal | all | `/about` | Version, correlation support info, legal links | P2 | ok, mobile |
| SCR-909 | Global app shell (nav frame) | all authenticated | (shell) | Header, side/bottom nav, language + RTL, user menu | P0 | load, ok, offline, mobile |

---

## 9. Coverage notes

- **Auth lifecycle** fully covered: register → verify → login → MFA → session-expiry → lockout →
  reset → logout (`SCR-001`–`SCR-011`, `SCR-040`, `SCR-046`, `SCR-903`–`SCR-905`).
- **Every state machine** in `00-foundational-decisions.md` §5 has authoring, transition, and outcome
  screens: onboarding (`SCR-100`–`SCR-110`, `SCR-300`–`SCR-309`), documents (`SCR-130`–`SCR-133`,
  `SCR-303`, `SCR-309`), RFQ (`SCR-410`–`SCR-423`), proposal (`SCR-150`–`SCR-157`), evaluation
  (`SCR-434`–`SCR-436`, `SCR-500`–`SCR-505`), award (`SCR-437`–`SCR-440`).
- **ERP boundary** surfaced but never blocking: `offline` state + `SCR-045`, `SCR-440`, `SCR-722`,
  `SCR-723`.
- **Mobile variants** are a required state on all supplier and shared screens (primary device = mobile
  + desktop); back-office/admin/governance remain responsive but desktop-optimized.
- **Global system states** (`SCR-041`–`SCR-047`) apply to any route.
- Detailed layouts for the twelve highest-value screens are in
  [`SCREEN-SPECIFICATIONS.md`](./SCREEN-SPECIFICATIONS.md).
