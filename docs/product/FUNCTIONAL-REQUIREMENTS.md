# Functional Requirements — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Product + Principal Architect · **Date:** 2026-08-26
> **Canonical sources (must remain consistent):**
> [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) ·
> [`DISCOVERY-REPORT.md`](./DISCOVERY-REPORT.md)
> **Related:** [`NON-FUNCTIONAL-REQUIREMENTS.md`](./NON-FUNCTIONAL-REQUIREMENTS.md) ·
> `BUSINESS-PROCESSES.md` (state machines) · `PERSONAS.md` · `../ux/DESIGN-SYSTEM.md` ·
> `../architecture/DOMAIN-MODEL.md` · [`../integration/`](../integration/)

---

## How to read this document

- Requirements are grouped by **capability area** and given a stable ID `FR-<AREA>-###`.
- **Actor(s)** use the canonical persona keys from the foundational brief (`supplier_admin`,
  `supplier_user`, `onboarding_reviewer`, `procurement_officer`, `procurement_manager`,
  `evaluator`, `ministry_viewer`, `system_admin`) plus `system` (automated/background) and
  `anonymous` (unauthenticated public).
- **Priority** uses **MoSCoW** (**M**ust / **S**hould / **C**ould / **W**on't-now).
- **Traces to** links each FR to one or more **Business Requirements (BR-###)** in the index below.
- All state names, transitions, aggregates, RBAC permissions, tokens, and NFR targets are taken
  verbatim from the canonical brief. Where a rule is Syria-specific and unconfirmed it is tagged
  **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** and mirrored in `ASSUMPTIONS.md`.

### Business Requirements (BR) index

This index is derived from the canonical brief so that every FR has a resolvable trace target. It is
the product-level "why" behind the functional "what".

| BR | Business requirement |
|---|---|
| BR-01 | Suppliers can self-onboard into a trusted registry with verified identity and compliant documents. |
| BR-02 | The portal operates fully standalone; ERP unavailability never blocks core flows. |
| BR-03 | Buying entities author, review, approve, and publish RFQs with configurable timelines. |
| BR-04 | Suppliers are invited to and respond to RFQs with structured, revisable proposals. |
| BR-05 | A structured clarification (Q&A) channel exists between buyers and suppliers per RFQ. |
| BR-06 | Proposals are evaluated by multiple evaluators against configurable weighted criteria, then consolidated. |
| BR-07 | Buyers compare proposals side-by-side and produce a defensible award recommendation and decision. |
| BR-08 | Every state change is permission-guarded, auditable, and attributable to an actor. |
| BR-09 | Access is role-based, least-privilege, and row-scoped by Supplier/Organization; Ministry is read-only. |
| BR-10 | The experience is Arabic-first, RTL/LTR, responsive, accessible (WCAG 2.2 AA), and premium. |
| BR-11 | All parties receive timely, relevant, multi-channel notifications with in-app history. |
| BR-12 | Each persona has a role-appropriate dashboard; Ministry has governance-level oversight. |
| BR-13 | Documents follow a managed lifecycle (required → uploaded → reviewed → approved/expiring/expired). |
| BR-14 | Suppliers maintain a rich profile and catalog of offerings mappable to buyer categories. |
| BR-15 | Approved supplier master and awarded POs sync asynchronously to/from ERPNext via ACL + Outbox. |
| BR-16 | Administrators configure reference data, evaluation templates, roles, and system settings. |
| BR-17 | Users can search and filter across suppliers, RFQs, proposals, and documents within their scope. |
| BR-18 | Localization: SYP default currency, multi-currency proposals, locale-aware dates/numerals. |
| BR-19 | Data protection, retention, and privacy are enforced; sensitive fields are access-controlled. |
| BR-20 | The platform is observable, resilient, and recoverable (backups + PITR). |

---

## 1. Identity & Access (IAM)

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-IAM-001 | Users authenticate with email + password via ASP.NET Core Identity; a JWT **access token** and a **rotating refresh token** are issued on success. | all authenticated | M | BR-09 |
| FR-IAM-002 | Refresh tokens rotate on use; a reused/revoked refresh token invalidates the token family and forces re-login. | `system` | M | BR-09, BR-19 |
| FR-IAM-003 | Password policy: minimum length, complexity, breached-password check, and server-side hashing (Identity default hasher). Account lockout after N failed attempts with backoff. | all | M | BR-09, BR-19 |
| FR-IAM-004 | **MFA-ready**: TOTP two-factor enrolment/verification (Identity 2FA) available; enforceable per role by policy (e.g. required for `system_admin`, `procurement_manager`). | all authenticated | S | BR-09 |
| FR-IAM-005 | Self-service password reset via time-limited, single-use email token; reset invalidates active sessions. | all | M | BR-09 |
| FR-IAM-006 | Email verification token issued at registration; email is verified before onboarding may progress past `EmailVerified`. | `supplier_admin`, `system` | M | BR-01 |
| FR-IAM-007 | Session management: users can view active sessions and sign out one/all devices; refresh tokens are revocable server-side. | all authenticated | S | BR-09, BR-19 |
| FR-IAM-008 | Authorization is **policy-based on permission claims** (`resource.action`); every protected endpoint declares required permission(s). | `system` | M | BR-08, BR-09 |
| FR-IAM-009 | **Row-scoping** is enforced server-side: suppliers see only their `SupplierId`; procurement/evaluators are scoped to their `OrganizationId`; ministry is read-only cross-org; admin is global. | `system` | M | BR-09 |
| FR-IAM-010 | UI hides unauthorized affordances by re-checking permissions client-side, but authorization is always re-enforced at the API (UI is never trusted). | `system` | M | BR-08, BR-09 |
| FR-IAM-011 | Identity provider is swappable to an external IdP (Keycloak/Entra) without changing authorization semantics. | `system_admin` | C | BR-09 |
| FR-IAM-012 | All authentication events (login success/failure, lockout, MFA, token refresh/revoke, password reset) are written to **AuditLog** with correlationId. | `system` | M | BR-08 |

## 2. Supplier Registration

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-REG-001 | A prospective supplier self-registers with organization name, primary representative name, email, phone, and password, creating a **Supplier** in `OnboardingState = Draft` and a `supplier_admin` user. | `anonymous` | M | BR-01 |
| FR-REG-002 | **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** Registration mode is configurable **open self-registration vs. invite-only**; default open. | `system_admin`, `anonymous` | M | BR-01, BR-16 |
| FR-REG-003 | On registration the system sends an email-verification link; verifying transitions `Draft → EmailVerified`. | `system`, `supplier_admin` | M | BR-01 |
| FR-REG-004 | Duplicate-prevention: registration is blocked/flagged when a supplier with the same legal identifier or email already exists (case/whitespace-normalized). | `system` | M | BR-01 |
| FR-REG-005 | Registration form is Arabic-first with English toggle, RTL layout, inline validation (Zod), and accessible error messaging. | `anonymous` | M | BR-10 |
| FR-REG-006 | Legal identifier fields (registration number, tax id, etc.) are captured **generically** with no invented Syrian validation rules. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `anonymous`, `supplier_admin` | M | BR-01 |
| FR-REG-007 | Unverified/abandoned `Draft` registrations are subject to a retention/cleanup policy (see FR-ADM-011). | `system` | S | BR-19 |

## 3. Onboarding

Mirrors the canonical state machine: `Draft → EmailVerified → ProfileInProgress → Submitted →
UnderReview → (InfoRequested → Resubmitted → UnderReview)* → Approved | Rejected`; post-approval
lifecycle `Active ↔ Suspended → Deactivated`.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-ONB-001 | After email verification the supplier completes profile sections; entering the flow transitions `EmailVerified → ProfileInProgress`. | `supplier_admin`, `supplier_user` | M | BR-01, BR-14 |
| FR-ONB-002 | A **completeness checklist** shows required profile sections and mandatory documents with live progress; submission is blocked until all required items are satisfied. | `supplier_admin` | M | BR-01, BR-13 |
| FR-ONB-003 | Supplier submits the application (`ProfileInProgress → Submitted`); the application becomes read-only to the supplier except where info is requested. | `supplier_admin` | M | BR-01 |
| FR-ONB-004 | A reviewer picks up a submission (`Submitted → UnderReview`), sees all sections/documents, and can approve, reject, or request more information. | `onboarding_reviewer` | M | BR-01, BR-13 |
| FR-ONB-005 | Reviewer requests changes with a structured reason and per-section/per-document annotations (`UnderReview → InfoRequested`); supplier is notified. | `onboarding_reviewer` | M | BR-01, BR-11 |
| FR-ONB-006 | Supplier addresses feedback and resubmits (`InfoRequested → Resubmitted → UnderReview`); the request/response loop may repeat and is fully audited. | `supplier_admin`, `supplier_user` | M | BR-01, BR-08 |
| FR-ONB-007 | Reviewer approves (`UnderReview → Approved`), moving the supplier to post-approval lifecycle `Active`; an ERP supplier-master sync event is enqueued to the **Outbox**. | `onboarding_reviewer`, `system` | M | BR-01, BR-15 |
| FR-ONB-008 | Reviewer rejects (`UnderReview → Rejected`) with a mandatory reason; supplier is notified and told whether re-application is permitted. | `onboarding_reviewer` | M | BR-01 |
| FR-ONB-009 | Post-approval lifecycle transitions `Active ↔ Suspended` (reversible, with reason) and `Suspended → Deactivated` (terminal) are permission-guarded and audited. | `onboarding_reviewer`, `procurement_manager`, `system_admin` | M | BR-01, BR-08 |
| FR-ONB-010 | Suspended/Deactivated suppliers cannot be invited to new RFQs or submit proposals; existing obligations are handled per policy. | `system` | M | BR-01, BR-09 |
| FR-ONB-011 | Every onboarding transition records actor, timestamp, from→to, reason, and correlationId in **AuditLog**; illegal transitions are rejected by the domain. | `system` | M | BR-08 |
| FR-ONB-012 | Reviewer work queue supports filtering, assignment, and SLA/age indicators for pending reviews. | `onboarding_reviewer` | S | BR-12, BR-17 |

## 4. Supplier Profile

Aggregate: **Supplier** — SupplierProfile, LegalInfo(VO), Address[], Contact[], Representative[],
Branch[], BankAccount[], CategoryLink[].

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-PROF-001 | Maintain core profile: legal/trade name (Arabic + English), description, logo, website, supplier type/group, default currency. | `supplier_admin`, `supplier_user` | M | BR-14 |
| FR-PROF-002 | Manage **LegalInfo** value object generically (registration number, tax id, incorporation date, legal form). No invented Syrian rules. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `supplier_admin` | M | BR-14 |
| FR-PROF-003 | Manage multiple **Addresses** (HQ, billing, branches) with type, region, and geo-fields; region references reference data. | `supplier_admin`, `supplier_user` | M | BR-14 |
| FR-PROF-004 | Manage multiple **Contacts** and **Representatives** with roles; designate a primary representative. | `supplier_admin` | M | BR-14 |
| FR-PROF-005 | Manage multiple **Branches**. | `supplier_admin` | S | BR-14 |
| FR-PROF-006 | Manage multiple **BankAccounts** (bank, IBAN/account no., currency, holder); bank fields are captured generically. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `supplier_admin` | M | BR-14, BR-15 |
| FR-PROF-007 | Manage **CategoryLinks** to the buyer **Category** tree (the goods/services the supplier provides). | `supplier_admin`, `supplier_user` | M | BR-14, BR-17 |
| FR-PROF-008 | **Delegated access:** `supplier_admin` invites/manages `supplier_user` accounts under the same `SupplierId` with scoped permissions. | `supplier_admin` | M | BR-09, BR-14 |
| FR-PROF-009 | Profile edits after approval that affect compliance-critical fields (legal id, bank, category) may re-trigger review and/or an ERP sync event. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `supplier_admin`, `system` | S | BR-13, BR-15 |
| FR-PROF-010 | The Supplier carries ERP mapping fields (`ExternalId?`, `SyncStatus`, `LastSyncedAt`, `RowVersion`) shown read-only to staff; concurrency is optimistic via `RowVersion`. | `system` | M | BR-15 |
| FR-PROF-011 | All profile text supports Arabic + English input with correct RTL/LTR rendering and tabular numerals for numeric fields. | `supplier_admin`, `supplier_user` | M | BR-10, BR-18 |

## 5. Documents

Mirrors the canonical state machine: `Required → Uploaded → UnderReview → Approved | Rejected(reason)`;
time-based `Approved → ExpiringSoon → Expired`. Rejected/Expired ⇒ profile flagged incomplete.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-DOC-001 | Required document set is derived from configurable **DocumentType** reference data; each shows state `Required` until uploaded. | `system`, `supplier_admin` | M | BR-13, BR-16 |
| FR-DOC-002 | Upload a document against a DocumentType (`Required → Uploaded`) with client + server validation of type, size, and MIME; virus/malware scan before acceptance. | `supplier_admin`, `supplier_user` | M | BR-13, BR-19 |
| FR-DOC-003 | Files persist via the **`IFileStorage`** abstraction (local disk dev / S3-compatible prod); documents are never served from a public bucket and require authorized, time-limited access. | `system` | M | BR-13, BR-19 |
| FR-DOC-004 | Capture optional **issue/expiry dates** per document to drive lifecycle timers. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** on which types expire. | `supplier_admin` | M | BR-13 |
| FR-DOC-005 | Reviewer reviews a document (`Uploaded → UnderReview → Approved | Rejected`); rejection requires a reason and flags the profile incomplete. | `onboarding_reviewer` | M | BR-13 |
| FR-DOC-006 | A scheduled (Hangfire) job transitions `Approved → ExpiringSoon` within a configurable window and `ExpiringSoon → Expired` at expiry, notifying the supplier and flagging the profile. | `system` | M | BR-13, BR-11 |
| FR-DOC-007 | Suppliers can re-upload a new version of an expired/rejected document; version history is retained and auditable. | `supplier_admin`, `supplier_user` | M | BR-13, BR-08 |
| FR-DOC-008 | Documents are downloadable only by authorized actors within scope; every view/download is audited. | `onboarding_reviewer`, `procurement_officer`, `supplier_admin` | M | BR-08, BR-19 |
| FR-DOC-009 | Document list shows state chips, expiry countdowns, and required-vs-optional grouping, fully localized and RTL-correct. | `supplier_admin` | S | BR-10, BR-13 |

## 6. Offerings

Aggregate child: **Offering[]** (catalog of goods/services), stored with flexible JSONB attributes.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-OFF-001 | Create/edit/deactivate an **Offering** (name AR/EN, description, category, unit of measure, optional indicative price + currency, flexible attributes). | `supplier_admin`, `supplier_user` | M | BR-14 |
| FR-OFF-002 | Offerings link to the buyer **Category** tree and **UnitOfMeasure** reference data. | `supplier_admin` | M | BR-14, BR-16 |
| FR-OFF-003 | Flexible per-category attributes are stored as JSONB and rendered with typed inputs. | `supplier_admin` | S | BR-14 |
| FR-OFF-004 | Offerings are discoverable by procurement in supplier search and inform RFQ invitation suggestions. | `procurement_officer` | S | BR-04, BR-17 |
| FR-OFF-005 | Offering visibility respects supplier lifecycle (only `Active` suppliers' offerings surface to buyers). | `system` | M | BR-01, BR-09 |

## 7. RFQ (authoring & lifecycle)

Mirrors the canonical state machine: `Draft → InternalReview → Approved → Published → SubmissionOpen
→ SubmissionClosed → UnderEvaluation → Clarification* → Shortlisting → Recommendation →
AwardApproval → Awarded → Completed`; `Cancelled` reachable from any pre-Awarded state (with reason
+ audit).

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-RFQ-001 | Create an RFQ (`Draft`) with title, description, buying `OrganizationId`, currency, and timeline (publish, submission open/close, target award). | `procurement_officer` | M | BR-03 |
| FR-RFQ-002 | Add **RfqItem[]** (line items: description, category, quantity, unit of measure, optional target/budget) and **Requirement[]** (technical/compliance requirements). | `procurement_officer` | M | BR-03 |
| FR-RFQ-003 | Attach RFQ documents/specifications (**Attachment[]**) via `IFileStorage`. | `procurement_officer` | M | BR-03 |
| FR-RFQ-004 | Bind an **EvaluationTemplate** (`EvaluationTemplateRef`) defining weighted criteria to be used for evaluation. | `procurement_officer` | M | BR-03, BR-06 |
| FR-RFQ-005 | Submit for internal review (`Draft → InternalReview`); an approver reviews (`InternalReview → Approved`) or returns to `Draft` with comments. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** single approver, configurable hierarchy. | `procurement_officer`, `procurement_manager` | M | BR-03, BR-08 |
| FR-RFQ-006 | Publish an approved RFQ (`Approved → Published`); publication is permission-guarded (`rfq.publish`) and audited. | `procurement_manager`, `procurement_officer` | M | BR-03 |
| FR-RFQ-007 | Submission window opens/closes by timeline (`Published → SubmissionOpen → SubmissionClosed`), driven by scheduled jobs; buyers may close early with reason. | `system`, `procurement_officer` | M | BR-03, BR-04 |
| FR-RFQ-008 | Move to evaluation (`SubmissionClosed → UnderEvaluation`) once submissions are frozen. | `procurement_officer` | M | BR-06 |
| FR-RFQ-009 | Progress through `Clarification* → Shortlisting → Recommendation → AwardApproval → Awarded → Completed` with each transition permission-guarded and audited. | `procurement_officer`, `procurement_manager` | M | BR-06, BR-07, BR-08 |
| FR-RFQ-010 | Cancel an RFQ from any pre-`Awarded` state with mandatory reason; invited suppliers are notified; state and reason are audited. | `procurement_manager` | M | BR-03, BR-08, BR-11 |
| FR-RFQ-011 | RFQ has an opaque public reference (e.g. `RFQ-2026-000123`); internal PKs are never exposed in URLs. | `system` | M | BR-08 |
| FR-RFQ-012 | Editing is constrained by state: full edit in `Draft`, restricted in `InternalReview`, locked after `Published` except addenda (see FR-CLR-004). | `procurement_officer` | M | BR-03 |
| FR-RFQ-013 | RFQ carries ERP mapping fields (`ExternalId?`, `SyncStatus`, `LastSyncedAt`, `RowVersion`). | `system` | S | BR-15 |

## 8. Invitations

Aggregate child: **Invitation[]** on RFQ (maps to ERPNext `Request for Quotation Supplier`).

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-INV-001 | Invite one or more `Active` suppliers to an RFQ, creating an **Invitation** per supplier with status tracking (invited, viewed, responding, submitted, declined). | `procurement_officer` | M | BR-04 |
| FR-INV-002 | Invitation candidate suggestions are drawn from supplier categories/offerings matching RFQ items. | `procurement_officer` | S | BR-04, BR-17 |
| FR-INV-003 | Each invited supplier is notified in-app + email with RFQ summary, timeline, and a deep link. | `system` | M | BR-04, BR-11 |
| FR-INV-004 | A supplier may **decline** an invitation with optional reason; declination is audited and visible to the buyer. | `supplier_admin` | S | BR-04, BR-08 |
| FR-INV-005 | Invitations can be added while `SubmissionOpen` (late invite) with adjusted deadline handling; not after `SubmissionClosed`. | `procurement_officer` | S | BR-04 |
| FR-INV-006 | Only invited suppliers can view RFQ details and submit a proposal; access is row-scoped and enforced server-side. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** open vs. invite-only visibility. | `system` | M | BR-04, BR-09 |
| FR-INV-007 | Invitation status is visible on the buyer RFQ dashboard (who was invited, viewed, responded). | `procurement_officer` | S | BR-12 |

## 9. Proposals

Aggregate: **Proposal** (one per Supplier per RFQ). State machine: `Draft → Submitted → UnderReview →
(ClarificationRequested → Revised → UnderReview)* → Shortlisted | NotSelected → AwardOffered →
Awarded | Declined`; supplier-initiated `Withdrawn` allowed while `SubmissionOpen`.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-PRP-001 | Start a **Proposal** (`Draft`) against an invited RFQ; exactly one proposal exists per Supplier per RFQ. | `supplier_admin`, `supplier_user` | M | BR-04 |
| FR-PRP-002 | Enter **ProposalItem[]** line pricing against RFQ items (unit price, quantity, currency, line totals) with automatic totals. | `supplier_admin`, `supplier_user` | M | BR-04, BR-18 |
| FR-PRP-003 | Capture **CommercialTerms** (VO: payment terms, delivery/lead time, incoterm, validity period) and a **TechnicalResponse** against requirements. | `supplier_admin`, `supplier_user` | M | BR-04 |
| FR-PRP-004 | Attach **ProposalDocument[]** (compliance/technical files) via `IFileStorage`. | `supplier_admin`, `supplier_user` | M | BR-04, BR-13 |
| FR-PRP-005 | **Draft safety:** proposal drafts auto-save/persist so work is never lost; drafts are private to the supplier until submitted. | `supplier_admin`, `supplier_user` | M | BR-04, BR-10 |
| FR-PRP-006 | Submit the proposal (`Draft → Submitted`) only while `SubmissionOpen`; on/after close, submission is rejected by the domain. Multi-currency proposals carry a display currency. | `supplier_admin`, `supplier_user` | M | BR-04, BR-18 |
| FR-PRP-007 | Pre-submission validation ensures all required RFQ items are priced and mandatory technical responses/documents are present. | `system` | M | BR-04 |
| FR-PRP-008 | Supplier may **withdraw** a submitted proposal while `SubmissionOpen` (`→ Withdrawn`), with reason and audit. | `supplier_admin` | S | BR-04, BR-08 |
| FR-PRP-009 | Buyer moves proposal to `UnderReview` after submission close as part of evaluation intake. | `procurement_officer` | M | BR-06 |
| FR-PRP-010 | Clarification loop: buyer requests clarification (`UnderReview → ClarificationRequested`), supplier revises (`Revised → UnderReview`), repeatable and audited (see §11). | `procurement_officer`, `supplier_admin` | M | BR-05, BR-06 |
| FR-PRP-011 | Outcome transitions `Shortlisted | NotSelected → AwardOffered → Awarded | Declined` are driven by evaluation/award and are permission-guarded. | `procurement_officer`, `procurement_manager`, `supplier_admin` | M | BR-07 |
| FR-PRP-012 | Submitted proposal contents are hidden from other suppliers at all times; buyer-side visibility respects evaluation blindness rules (see §11). | `system` | M | BR-06, BR-09, BR-19 |
| FR-PRP-013 | Proposal carries ERP mapping fields (`ExternalId?`, `SyncStatus`, `LastSyncedAt`, `RowVersion`). | `system` | C | BR-15 |

## 10. Clarifications (RFQ Q&A)

Aggregate child: **Clarification[]** on RFQ (buyer↔supplier Q&A during the RFQ lifecycle).

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-CLR-001 | Invited suppliers post clarification **questions** against a published RFQ during the clarification window. | `supplier_admin`, `supplier_user` | M | BR-05 |
| FR-CLR-002 | Buyer answers questions; answers can be **private** (to the asker) or **published** to all invited suppliers. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** default publish-to-all for fairness. | `procurement_officer` | M | BR-05 |
| FR-CLR-003 | A published clarification thread is visible to all invited suppliers with the asker anonymized. | `supplier_admin`, `supplier_user` | S | BR-05, BR-09 |
| FR-CLR-004 | Buyer can issue an RFQ **addendum** (spec/timeline change) that notifies all invited suppliers and is recorded on the RFQ timeline. | `procurement_officer` | S | BR-03, BR-05 |
| FR-CLR-005 | Clarification windows are bounded by RFQ timeline; posting outside the window is rejected. | `system` | M | BR-05 |
| FR-CLR-006 | All clarification activity is audited and notified. | `system` | M | BR-08, BR-11 |

## 11. Evaluation

Aggregate: **Evaluation** — EvaluationAssignment[], EvaluatorScore[], ConsolidatedResult. State
machine: `NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated → Finalized`.
**[ASSUMPTION]** evaluators score **independently (blind to peers)** before consolidation.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-EVL-001 | Assign evaluators to an RFQ's evaluation (`NotStarted → Assigned`), creating an **EvaluationAssignment** per evaluator. | `procurement_manager`, `procurement_officer` | M | BR-06 |
| FR-EVL-002 | The evaluation uses the RFQ-bound **EvaluationTemplate** (Criterion[]: name, weight, max, threshold, scoring type). | `system` | M | BR-06 |
| FR-EVL-003 | An evaluator opens their assignment (`Assigned → InProgress`) and scores each proposal per criterion, with optional comments per criterion. | `evaluator` | M | BR-06 |
| FR-EVL-004 | **Blind scoring:** an evaluator cannot see peers' scores/comments before submission; evaluators may be shielded from supplier identity/commercials per configuration. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `system` | M | BR-06, BR-19 |
| FR-EVL-005 | Weighted score computation applies criterion weights and enforces per-criterion **thresholds** (a below-threshold criterion flags/disqualifies per template rule). | `system` | M | BR-06 |
| FR-EVL-006 | Evaluator submits their scores (`InProgress → EvaluatorSubmitted`), after which their scores are locked (reopen only via permissioned override with audit). | `evaluator` | M | BR-06, BR-08 |
| FR-EVL-007 | Once all evaluators submit, results are **consolidated** (`EvaluatorSubmitted → Consolidated`) into a **ConsolidatedResult** (aggregate/weighted-average per proposal, ranking). | `procurement_officer`, `system` | M | BR-06 |
| FR-EVL-008 | Consolidated evaluation is finalized (`Consolidated → Finalized`), unlocking shortlisting/recommendation on the RFQ. | `procurement_manager` | M | BR-06, BR-07 |
| FR-EVL-009 | Scoring is only permitted with `evaluation.score`; all scoring/submission/consolidation/override actions are audited with actor + correlationId. | `system` | M | BR-08, BR-09 |
| FR-EVL-010 | Evaluator UI is desktop/tablet-optimized, RTL-correct, keyboard-navigable, and shows criteria, weights, thresholds, and progress. | `evaluator` | M | BR-06, BR-10 |
| FR-EVL-011 | Handling of a non-responding evaluator (reassign/exclude with quorum rule) is supported. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `procurement_manager` | C | BR-06 |

## 12. Comparison

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-CMP-001 | Side-by-side **comparison matrix** of all submitted proposals for an RFQ (line prices, totals, commercial terms, technical responses, evaluation scores/ranking). | `procurement_officer`, `procurement_manager` | M | BR-07 |
| FR-CMP-002 | Comparison highlights lowest/best per line, weighted-score ranking, and threshold pass/fail flags. | `procurement_officer` | S | BR-07 |
| FR-CMP-003 | Multi-currency comparison normalizes to a chosen display currency with the applied rate shown. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** on rate source. | `procurement_officer` | S | BR-07, BR-18 |
| FR-CMP-004 | Comparison view honors evaluation blindness rules until finalized and respects viewer permissions/scope. | `system` | M | BR-06, BR-09 |
| FR-CMP-005 | Comparison is exportable (PDF/print) for the award file, localized and RTL-correct. | `procurement_officer` | C | BR-07, BR-10 |
| FR-CMP-006 | Comparison table is responsive and RTL-aware with sticky headers and tabular numerals for prices. | `procurement_officer` | S | BR-10, BR-18 |

## 13. Procurement Workflow (orchestration)

Cross-cutting orchestration binding RFQ, Invitations, Proposals, Clarifications, and Evaluation.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-PWF-001 | The RFQ workspace presents the full lifecycle as guided stages with the current `RfqState`, permitted next actions, and blockers. | `procurement_officer` | M | BR-03, BR-12 |
| FR-PWF-002 | Stage gates enforce prerequisites (e.g. cannot enter `UnderEvaluation` before `SubmissionClosed`; cannot recommend before evaluation `Finalized`). | `system` | M | BR-03, BR-06, BR-07 |
| FR-PWF-003 | All workflow actions are permission-guarded and produce audit + notification events. | `system` | M | BR-08, BR-11 |
| FR-PWF-004 | Timeline automation (open/close submission, clarification window, reminders) runs via scheduled jobs and is resilient to restarts (Hangfire durable). | `system` | M | BR-03, BR-20 |
| FR-PWF-005 | Concurrency: simultaneous edits are guarded by `RowVersion`; conflicting writes surface a clear, localized conflict resolution prompt. | `system` | S | BR-08 |

## 14. Award

Aggregate: **Award** — Recommendation, Approval[], AwardDecision, `ExternalPurchaseOrderRef?`. State
machine: `Recommended → PendingApproval → Approved | Rejected → Awarded → (Outbox → ERP PO)`.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-AWD-001 | Officer produces a **Recommendation** (`Recommended`) selecting the winning proposal(s) with justification grounded in the evaluation/comparison. | `procurement_officer` | M | BR-07 |
| FR-AWD-002 | Recommendation is routed for approval (`Recommended → PendingApproval`). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** single approver, configurable multi-step hierarchy. | `procurement_officer` | M | BR-07, BR-16 |
| FR-AWD-003 | Approver approves or rejects (`PendingApproval → Approved | Rejected`) with mandatory reason; rejection returns to recommendation with feedback. | `procurement_manager` | M | BR-07, BR-08 |
| FR-AWD-004 | On approval, the award is issued (`Approved → Awarded`); the winning proposal transitions to `AwardOffered/Awarded` and non-winning proposals to `NotSelected`; all suppliers are notified. | `procurement_manager`, `system` | M | BR-07, BR-11 |
| FR-AWD-005 | Awarding enqueues an **Outbox** event that the integration layer translates to an **ERP Purchase Order**; `ExternalPurchaseOrderRef` is stored on write-back. The flow never blocks on ERP availability. | `system` | M | BR-15, BR-02 |
| FR-AWD-006 | RFQ transitions `AwardApproval → Awarded → Completed` in step with the Award aggregate. | `procurement_officer`, `system` | M | BR-07 |
| FR-AWD-007 | Award actions require `award.approve` / relevant permissions and are fully audited with actor, decision, reason, correlationId. | `system` | M | BR-08, BR-09 |
| FR-AWD-008 | Award outcome, justification, and comparison snapshot are retained as the immutable award file. | `system` | S | BR-07, BR-08 |

## 15. Notifications

Aggregate: **Notification**. Multi-channel (in-app + email; SMS future) with in-app history.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-NOT-001 | The system emits notifications for lifecycle events across onboarding, documents, RFQ, invitations, proposals, clarifications, evaluation, and award. | `system` | M | BR-11 |
| FR-NOT-002 | In-app notification center shows unread/read state, grouping, deep links, and history; fully localized and RTL-correct. | all authenticated | M | BR-11, BR-10 |
| FR-NOT-003 | Email notifications use localized (AR/EN) templates matching the recipient's locale; delivery is via a durable background job with retry. | `system` | M | BR-11, BR-18 |
| FR-NOT-004 | Users manage notification preferences per category/channel (opt-out of non-critical only). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | all authenticated | S | BR-11, BR-19 |
| FR-NOT-005 | Notification generation is decoupled from core transactions via the **Outbox** so a channel outage never blocks a domain action. | `system` | M | BR-11, BR-02 |
| FR-NOT-006 | Deadline reminders (submission closing, document expiring, review pending) are scheduled and de-duplicated. | `system` | S | BR-11, BR-13 |
| FR-NOT-007 | **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** SMS channel is designed-for but disabled until a provider is confirmed. | `system` | C | BR-11 |

## 16. Dashboards

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-DSH-001 | **Supplier dashboard:** onboarding/profile completeness, document status/expiries, active invitations, proposal statuses, and awards. | `supplier_admin`, `supplier_user` | M | BR-12 |
| FR-DSH-002 | **Onboarding/compliance dashboard:** review queue, SLA/aging, pending info-requests, document-expiry watchlist. | `onboarding_reviewer` | M | BR-12 |
| FR-DSH-003 | **Procurement dashboard:** RFQ pipeline by state, submissions received, evaluation progress, pending approvals, upcoming deadlines. | `procurement_officer`, `procurement_manager` | M | BR-12 |
| FR-DSH-004 | **Evaluator dashboard:** assigned evaluations, scoring progress, and deadlines. | `evaluator` | M | BR-12 |
| FR-DSH-005 | **Ministry governance dashboard:** cross-organization, **read-only** aggregate metrics (RFQ volumes, cycle times, participation, awards). **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** whether commercial values are visible or only aggregate/anonymized. | `ministry_viewer` | M | BR-12, BR-09 |
| FR-DSH-006 | **Admin dashboard:** users/roles, reference-data health, integration/outbox status, job health, audit access. | `system_admin` | S | BR-12, BR-16, BR-20 |
| FR-DSH-007 | Dashboards use themeable Recharts/bespoke SVG, respect design tokens, are responsive, RTL-correct, and accessible. | all authenticated | M | BR-10, BR-12 |
| FR-DSH-008 | Every dashboard widget is row-scoped so a user only sees data within their permission scope. | `system` | M | BR-09 |

## 17. Search

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-SRCH-001 | Search suppliers by name, category, offering, region, and lifecycle state within the actor's scope. | `procurement_officer`, `onboarding_reviewer` | M | BR-17 |
| FR-SRCH-002 | Search/filter RFQs by state, organization, category, and timeline within scope. | `procurement_officer`, `ministry_viewer` | M | BR-17 |
| FR-SRCH-003 | Suppliers search their invitations/proposals/documents by state and RFQ. | `supplier_admin`, `supplier_user` | M | BR-17 |
| FR-SRCH-004 | List/table views provide server-side pagination, sorting, and faceted filters (TanStack Table), RTL-aware and accessible. | all authenticated | M | BR-17, BR-10 |
| FR-SRCH-005 | Search honors row-scoping so results never leak cross-scope data. | `system` | M | BR-09, BR-17 |
| FR-SRCH-006 | Full-text search across documents/proposals is available where indexed. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** on scope/indexing. | `procurement_officer` | C | BR-17 |

## 18. Administration

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-ADM-001 | Manage **Users** (create, invite, disable, reset) and assign roles within scope. | `system_admin` | M | BR-09, BR-16 |
| FR-ADM-002 | Manage **Roles** and their **Permissions** (`resource.action` sets); seed default per-persona roles, admin-editable. | `system_admin` | M | BR-09, BR-16 |
| FR-ADM-003 | Manage **Organizations** and OrgUnits (buying entities: Hotel / MOT body / Ministry), including Supplier↔Organization many-to-many relationships. | `system_admin` | M | BR-16, BR-15 |
| FR-ADM-004 | Manage **Category** tree, **DocumentType**, Currency, UnitOfMeasure, Incoterm, Region reference data. | `system_admin` | M | BR-16 |
| FR-ADM-005 | Manage **EvaluationTemplates** (criteria, weights, max, thresholds, scoring types) reusable across RFQs. | `system_admin`, `procurement_manager` | M | BR-06, BR-16 |
| FR-ADM-006 | Configure system settings: registration mode, default currency, numeral system, document-expiry windows, approval hierarchy. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** | `system_admin` | M | BR-16, BR-18 |
| FR-ADM-007 | Configure notification templates (AR/EN) and channel enablement. | `system_admin` | S | BR-11, BR-16 |
| FR-ADM-008 | View integration/Outbox health, retry/dead-letter management, and ERP sync status. | `system_admin` | S | BR-15, BR-20 |
| FR-ADM-009 | View background-job (Hangfire) dashboard health and scheduled tasks. | `system_admin` | S | BR-20 |
| FR-ADM-010 | All administrative changes are permission-guarded (`admin.*`) and audited. | `system` | M | BR-08, BR-16 |
| FR-ADM-011 | Configure and run retention/cleanup policies (abandoned drafts, expired tokens) with audit. | `system_admin` | S | BR-19 |

## 19. Audit

Aggregate: **AuditLog** (append-only).

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-AUD-001 | Every state transition and sensitive action records actor, timestamp, from→to, reason, and correlationId. | `system` | M | BR-08 |
| FR-AUD-002 | Audit log is **append-only/immutable**; no user (including admin) can edit or delete entries. | `system` | M | BR-08, BR-19 |
| FR-AUD-003 | Authorized users read audit entries within scope via `audit.read`; suppliers see their own activity trail. | `system_admin`, `procurement_manager`, `ministry_viewer` | M | BR-08, BR-09 |
| FR-AUD-004 | Audit entries are filterable/searchable by entity, actor, action, and date range and are exportable for governance. | `system_admin`, `ministry_viewer` | S | BR-08, BR-12 |
| FR-AUD-005 | Audit records correlate to distributed traces (OpenTelemetry correlationId) for end-to-end investigation. | `system` | S | BR-08, BR-20 |
| FR-AUD-006 | Document view/download and data exports are audited as access events. | `system` | M | BR-08, BR-19 |

## 20. Integration (ERPNext, async)

Anti-Corruption Layer (ACL) + transactional **Outbox** + adapters. Async-by-default; portal never
blocks on ERP.

| ID | Description | Actor(s) | Priority | Traces to |
|---|---|---|---|---|
| FR-INT-001 | Domain/integration events are written **transactionally** to the **Outbox** in the same transaction as the state change. | `system` | M | BR-15, BR-08 |
| FR-INT-002 | A background dispatcher publishes Outbox messages to ERP adapters with **durable retries, backoff, and dead-letter**. | `system` | M | BR-15, BR-20 |
| FR-INT-003 | On supplier **approval**, an approved-supplier-master sync event maps portal Supplier → ERPNext `Supplier` (superset fields → mapped subset). | `system` | M | BR-15 |
| FR-INT-004 | On **award**, an award event is translated by the ACL to an ERPNext **Purchase Order**; the returned PO key is stored as `ExternalPurchaseOrderRef`/`ExternalId`. | `system` | M | BR-15 |
| FR-INT-005 | Every ERP-synced entity stores a nullable **`ExternalId` (string)**; the portal **never** uses an integer FK to ERP. | `system` | M | BR-15 |
| FR-INT-006 | Sync fields (`SyncStatus`, `LastSyncedAt`, `RowVersion`) are maintained and surfaced to admins; conflicts are detected and queued, never silently overwritten. | `system` | M | BR-15, BR-20 |
| FR-INT-007 | The portal remains fully functional (registration→award) when the ERP is unavailable; pending syncs drain when ERP returns. | `system` | M | BR-02, BR-15 |
| FR-INT-008 | Inbound reference/master updates from ERP (if enabled) pass through the ACL and never corrupt portal domain invariants. **[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]** direction/scope of inbound sync. | `system` | C | BR-15 |
| FR-INT-009 | Integration adapters are swappable and versioned; contract mismatches fail safe (dead-letter + alert), not silently. | `system` | S | BR-15, BR-20 |

---

## Traceability summary

- Every FR traces to at least one BR-### above; every BR-### is exercised by at least one FR.
- Every canonical **state machine** (§5 of the foundational brief) is covered: onboarding (§3),
  documents (§5), RFQ (§7/§13), proposal (§9), evaluation (§11), award (§14).
- Every canonical **aggregate** (§4 of the foundational brief) has owning FRs.
- RBAC (`resource.action`), row-scoping, audit-on-transition, and the ERP `ExternalId`/Outbox
  boundary are enforced as cross-cutting Musts, consistent with the canonical brief.
- Non-functional targets for these behaviors are specified in
  [`NON-FUNCTIONAL-REQUIREMENTS.md`](./NON-FUNCTIONAL-REQUIREMENTS.md).
