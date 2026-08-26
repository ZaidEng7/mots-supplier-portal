# MOTS Supplier Portal — Business Rules Catalog

> **Status:** Baseline v1 · **Phase:** 0 (Discovery) · **Owner:** Principal Architect · **Date:** 2026-08-26
>
> Canonical business rules for the portal. Must remain consistent with
> [`../architecture/00-foundational-decisions.md`](../architecture/00-foundational-decisions.md)
> (state machines §5, RBAC §6, ERP boundary §1, localization §8). Processes and transitions are in
> [`BUSINESS-PROCESSES.md`](BUSINESS-PROCESSES.md); assumptions in [`ASSUMPTIONS.md`](ASSUMPTIONS.md);
> open items in [`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md).

## Conventions

- Each rule has a stable id **`BRULE-###`**, a **statement**, a **rationale**, an **enforcement point**,
  and — where relevant — an `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` tag.
- **Enforcement point** legend:
  - **Domain** — invariant guarded inside an aggregate; illegal input rejected by the model.
  - **API** — request-pipeline validation (FluentValidation) and/or authorization policy handler.
  - **UI** — client validation / affordance-hiding only; **never** the sole enforcement.
  - **System** — scheduled/background enforcement (Hangfire) or Outbox/ACL.
- **No invented Syrian legal/regulatory/tax rules.** Where a legal requirement is unknown, the field or
  flow is captured **generically** and tagged `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`.
- Numbers marked `[ASSUMPTION]` (windows, thresholds, sizes) are **configurable defaults**, not law.

## Rule areas index

| Area | Range |
|---|---|
| A. Identity, registration & eligibility | BRULE-001 … BRULE-015 |
| B. Documents: requirements & expiry | BRULE-016 … BRULE-028 |
| C. RFQ authoring, publication & invitations | BRULE-029 … BRULE-041 |
| D. Proposals: submission, deadlines, revision & withdrawal | BRULE-042 … BRULE-057 |
| E. Evaluation: independence, scoring & consolidation | BRULE-058 … BRULE-070 |
| F. Award, approval thresholds & constraints | BRULE-071 … BRULE-083 |
| G. Data visibility, scoping & confidentiality | BRULE-084 … BRULE-094 |
| H. Audit, notifications & integrity (cross-cutting) | BRULE-095 … BRULE-100 |

---

## A. Identity, Registration & Eligibility

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-001** | A supplier organization is represented by exactly one **Supplier Admin** at registration; additional **Supplier Users** are invited by that admin. | Clear accountability and delegation model per personas (`supplier_admin`/`supplier_user`). | Domain + API | — |
| **BRULE-002** | Registration requires a **unique, verifiable email**; the account cannot progress past `Draft` until the email is verified. | Prevent duplicate/fraudulent accounts; establish a reliable contact channel. | Domain + API + System | Matches onboarding `Draft → EmailVerified`. |
| **BRULE-003** | Supplier self-registration is **open** by default; the platform can be switched to **invite-only** per buying-entity policy. | Two tenancy/onboarding models seen in discovery. | API (config) | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` |
| **BRULE-004** | A supplier profile may be **submitted for review** only when all **mandatory** profile fields are complete, all **required** documents are at least `Uploaded`, and at least one active `Representative` exists. | Guarantees reviewers receive a complete case. | Domain + API + UI | Gate for `ProfileInProgress → Submitted`. |
| **BRULE-005** | Legal/registration/tax identifiers (e.g. commercial registration no., tax id) are captured as **generic, typed fields**; their presence/format requirements are configuration, not hard-coded law. | No invented Syrian legal rules (canonical §8). | API (config) | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` |
| **BRULE-006** | Only suppliers in `OnboardingState = Approved/Active` are **eligible** to be invited to RFQs and to submit proposals. | Ensures only vetted suppliers participate. | Domain + API | Ties eligibility to §5 lifecycle. |
| **BRULE-007** | A `Suspended` supplier **cannot** be newly invited or submit **new** proposals; handling of in-flight proposals follows suspension policy. | Compliance enforcement without silent data loss. | Domain + API | `[ASSUMPTION]` treatment of open proposals. |
| **BRULE-008** | A `Deactivated` supplier is excluded from all new selection and its logins are revoked; historical records are retained. | Clean exit while preserving audit/history. | Domain + API | Soft lifecycle state; not hard delete. |
| **BRULE-009** | A supplier must accept the current **Terms & Conditions / data-processing notice** before first submission; version and timestamp are recorded. | Consent traceability. | Domain + API | `[ASSUMPTION]` T&C content owned by business. |
| **BRULE-010** | Each supplier maps to **one or more** buying Organizations (many-to-many capable). | ERPNext multi-company supplier fact (discovery §3.2). | Domain | — |
| **BRULE-011** | A supplier's **`ExternalId`** is assigned **only after onboarding approval** and successful ERP upsert ACK; it is a **string**, nullable until then, never an integer FK. | ERP boundary (canonical §1). | Domain + System | — |
| **BRULE-012** | Rejected onboarding is terminal for that application; re-application policy (allowed / cooldown) is configurable. | Avoids ambiguous limbo states. | Domain + API | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` |
| **BRULE-013** | Email-verification tokens expire after a fixed TTL and are single-use; expired tokens can be re-issued on request. | Security of the verification channel. | API + System | `[ASSUMPTION]` TTL default 24–72h. |
| **BRULE-014** | Bank account details captured during onboarding are **masked** in list/detail views and access-restricted to authorized roles. | Sensitive financial PII protection (OWASP ASVS L2). | API + UI | Full value never in logs/URLs. |
| **BRULE-015** | A representative must have a valid contact (email/phone) and role; only representatives can act as proposal signatories. | Accountable, contactable counterparties. | Domain + API | — |

---

## B. Documents: Requirements & Expiry

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-016** | The **required document set** is derived from active `DocumentType` reference data and the supplier's category; it is configuration, not code. | Requirements vary and change without redeploy. | Domain + API (config) | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on the actual list. |
| **BRULE-017** | Onboarding **cannot be approved** while any **required** document is missing, `Rejected`, or `Expired`. | Compliance completeness gate. | Domain + API | Aligns `UnderReview → Approved` guard. |
| **BRULE-018** | A `Rejected` or `Expired` document **flags the profile incomplete** until replaced with an approved version. | Canonical document machine (§5). | Domain | — |
| **BRULE-019** | Document uploads are restricted to allowed MIME types and a maximum size, and must pass an **anti-virus scan** before acceptance. | Storage integrity and malware defense. | API + System | `[ASSUMPTION]` allowed types PDF/JPG/PNG; max size default 10 MB. |
| **BRULE-020** | Documents whose `DocumentType` requires expiry must have a valid **future expiry date** at upload; types without expiry never enter `ExpiringSoon/Expired`. | Correct lifecycle behavior. | Domain + API | — |
| **BRULE-021** | A document enters **`ExpiringSoon`** when `today ≥ expiry − reminderWindow`. | Proactive renewal. | System | `[ASSUMPTION]` reminder window default 30 days. |
| **BRULE-022** | A document becomes **`Expired`** when `today > expiry`; expiry is evaluated by a daily idempotent background sweep. | Deterministic, server-authoritative expiry. | System | Hangfire recurring job. |
| **BRULE-023** | Expiry of a document tagged **award-critical** may auto-suspend the supplier (`Active → Suspended`). | Prevent awarding to non-compliant suppliers. | Domain + System | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` which types are award-critical. |
| **BRULE-024** | Uploading over an approved document creates a **new version** and returns that document to `Uploaded → UnderReview`; prior versions are retained. | Auditability and no destructive overwrite. | Domain | — |
| **BRULE-025** | Renewal reminders escalate (e.g. 30/14/3 days) via email + in-app until renewed. | Reduce lapse rate. | System | `[ASSUMPTION]` cadence. |
| **BRULE-026** | Only `onboarding_reviewer` (or `system_admin`) may approve/reject documents; suppliers may only upload/replace. | Segregation between submitter and verifier. | API (policy) | `supplier.document.*` permissions. |
| **BRULE-027** | Rejecting a document **requires a reason**, surfaced to the supplier. | Actionable feedback; audit completeness. | Domain + API | — |
| **BRULE-028** | Document files are stored via the `IFileStorage` abstraction (local/dev, S3-compatible/prod); URLs are opaque and access-controlled, never public. | Storage independence (canonical §2) + confidentiality. | API + System | Signed, expiring access. |

---

## C. RFQ Authoring, Publication & Invitations

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-029** | An RFQ is created and owned by a `procurement_officer` and is **scoped to their Organization**; cross-org authoring is prohibited. | RBAC row-scoping (canonical §6). | API (policy) | — |
| **BRULE-030** | An RFQ may enter `InternalReview` only with ≥1 `RfqItem`, a bound `EvaluationTemplateRef`, and **valid future** submission open/close dates. | Prevents publishing incomplete tenders. | Domain + API | Gate for `Draft → InternalReview`. |
| **BRULE-031** | An RFQ may be **published** only after `procurement_manager` approval. | Internal control before external exposure. | Domain + API (policy) | `rfq.approve` then `rfq.publish`. |
| **BRULE-032** | On publish, invitations may be sent **only to `Active` suppliers**; suspended/deactivated suppliers are excluded. | Eligibility (BRULE-006/007). | Domain + API | — |
| **BRULE-033** | `submissionCloseAt` must be strictly after `submissionOpenAt`, and both must respect a **minimum open window**. | Fair opportunity to respond. | Domain + API | `[ASSUMPTION]` minimum window (e.g. 3 business days). |
| **BRULE-034** | Submission windows are enforced **server-side**; client countdowns are advisory and non-authoritative. | Prevent clock-based bypass. | System + Domain | — |
| **BRULE-035** | Deadline **extension** is allowed while `Published`/`SubmissionOpen` by `procurement_officer` and notifies all invitees; **shortening** requires `procurement_manager`. | Fairness and transparency. | API (policy) + System | `[ASSUMPTION]`. |
| **BRULE-036** | Clarification **Q&A** is available during the open window; answers deemed material are broadcast to **all** invitees (anonymized questioner). | Level playing field. | API + UI | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` broadcast policy. |
| **BRULE-037** | An RFQ can be **Cancelled** from any pre-`Awarded` state with a **mandatory reason**; cancellation voids open invitations/proposals and notifies all parties. | Canonical §5 cancellation path. | Domain + API + System | — |
| **BRULE-038** | A published RFQ cannot be **materially amended** in place; material changes require an **addendum + re-notification** (and optionally re-acknowledgement). | Integrity of a live tender. | Domain + API | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. |
| **BRULE-039** | Evaluation cannot open unless there is **≥1 `Submitted` proposal**; otherwise the officer cancels or re-tenders. | Avoids empty evaluations. | Domain + API | `[ASSUMPTION]`. |
| **BRULE-040** | Each RFQ carries an opaque public reference `RFQ-YYYY-NNNNNN`; internal PKs (GUIDv7) are never exposed in URLs. | Canonical §4 identifier policy. | Domain + API | — |
| **BRULE-041** | The `EvaluationTemplate` (criteria, weights, thresholds) is **snapshotted** onto the RFQ at publish; later template edits do not retroactively change a live RFQ. | Reproducible, fair evaluation basis. | Domain | — |

---

## D. Proposals: Submission, Deadlines, Revision & Withdrawal

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-042** | A supplier may hold **at most one** proposal per RFQ (uniqueness on `SupplierId + RfqId`). | Prevents split/duplicate bids. | Domain + DB constraint | — |
| **BRULE-043** | Only an **invited, `Active`** supplier may create a proposal for an RFQ. | Eligibility + invitation gate. | Domain + API | — |
| **BRULE-044** | Proposals are freely editable only while `Draft` **and** the RFQ is `SubmissionOpen`. | Clear editing window. | Domain + API | — |
| **BRULE-045** | Submission requires: all required items priced, mandatory documents attached, `Validity` ≥ RFQ minimum, and T&C accepted. | Complete, comparable bids. | Domain + API + UI | — |
| **BRULE-046** | **Late submissions are rejected** by the domain when `now ≥ submissionCloseAt`. | Deadline integrity (BRULE-034). | Domain + System | — |
| **BRULE-047** | A supplier may **withdraw** a `Draft`/`Submitted` proposal **only while the RFQ is `SubmissionOpen`**; after close, withdrawal is blocked. | Canonical proposal machine (§5) withdrawal window. | Domain + API | — |
| **BRULE-048** | After withdrawal within the open window, the supplier may start and submit a **new** proposal (subject to uniqueness on re-creation). | Correct a mistaken submission before deadline. | Domain | `[ASSUMPTION]`. |
| **BRULE-049** | After submission, changes occur **only** via `ClarificationRequested → Revised`, limited to the requested scope. | Prevents post-deadline free editing. | Domain + API | — |
| **BRULE-050** | Whether **commercial (price) revisions** are permitted during clarification is a configurable policy; default is **technical-only** clarification (price locked). | Procurement fairness. | Domain + API | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. |
| **BRULE-051** | Each submission/revision is **snapshotted** with an incrementing revision number and timestamp; prior revisions are immutable and retained. | Auditability and dispute resolution. | Domain | — |
| **BRULE-052** | Unsubmitted `Draft` proposals **auto-lapse** (closed, non-considered) at `SubmissionClosed`. | Clean evaluation input set. | System | `[ASSUMPTION]`. |
| **BRULE-053** | A supplier sees **only its own** proposal(s); no cross-supplier visibility of existence, content, or pricing at any time. | Confidentiality (see area G). | API (row-scope) | — |
| **BRULE-054** | Proposal currency defaults to **SYP** and is configurable; multi-currency proposals carry an explicit display currency. | Canonical §8 localization. | Domain + API | Conversion basis is `[ASSUMPTION]`. |
| **BRULE-055** | Prices and quantities use validated numeric ranges; totals are computed server-side, never trusted from the client. | Integrity of commercial data. | Domain + API | Tabular figures in UI (§7). |
| **BRULE-056** | On RFQ cancellation, all non-terminal proposals move to a closed/`NotSelected` outcome with mandatory notification. | Consistent teardown. | Domain + System | — |
| **BRULE-057** | A supplier may **Decline** an `AwardOffered` proposal within the acceptance window; this frees the award for an alternate. | Real-world award refusal. | Domain + API | `[ASSUMPTION]` acceptance window. |

---

## E. Evaluation: Independence, Scoring & Consolidation

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-058** | Evaluators score **independently and blind to peers** while `InProgress`/`EvaluatorSubmitted`; peer scores/comments are not readable until `Consolidated`. | Canonical §5 independence assumption; bias reduction. | API (row-scope) + Domain | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. |
| **BRULE-059** | Only assigned `evaluator`s may score, and only the proposals assigned to them, and only while the RFQ is `UnderEvaluation`. | RBAC + scoping + lifecycle. | API (policy) | `evaluation.score`. |
| **BRULE-060** | Each per-criterion score must lie within `[0 .. Criterion.max]`; scoring types (numeric/scale) come from the template. | Valid, comparable inputs. | Domain + API + UI | — |
| **BRULE-061** | Criteria requiring justification cannot be submitted without a comment. | Defensible decisions. | Domain + API | `[ASSUMPTION]` which criteria require comment. |
| **BRULE-062** | An evaluator can submit only when **all** their assigned proposals are fully scored; submission **locks** their scores. | Complete, immutable evaluator input. | Domain + API | — |
| **BRULE-063** | The **consolidated score** per criterion combines evaluator scores (default **average**), multiplies by `Criterion.weight`, and sums to a weighted total. | Weighted scorecard model (discovery §3.2). | Domain | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` on aggregation (average vs other). |
| **BRULE-064** | **Threshold gating**: a proposal failing any criterion-level threshold or the overall minimum is **not shortlist-eligible**, regardless of total. | Enforce minimum standards. | Domain | Thresholds from template snapshot (BRULE-041). |
| **BRULE-065** | Sum of criterion weights in a template must equal **100%** (or normalize) before it can be bound to an RFQ. | Well-formed scoring. | Domain + API | — |
| **BRULE-066** | Consolidation requires all assigned evaluators submitted, or an explicit **quorum** decision by `procurement_manager`. | Handle absent evaluators without deadlock. | Domain + API | `[ASSUMPTION]` quorum policy. |
| **BRULE-067** | An evaluator with a **conflict of interest** must be recused (unassigned) before submitting; recusal is audited. | Integrity of the committee. | API + Domain | `[ASSUMPTION]`. |
| **BRULE-068** | Consolidated/finalized results are **read-only**; re-opening requires a `procurement_manager` action with a mandatory reason. | Protect the decision record. | Domain + API | — |
| **BRULE-069** | **Ranking tie-breaks** apply a defined order (e.g. highest technical score, then lowest compliant price, then earliest submission). | Deterministic, explainable outcomes. | Domain | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` tie-break order. |
| **BRULE-070** | The recommender/approver must **not** have scored as an evaluator on the same RFQ where policy forbids it. | Segregation of duties across roles. | API (policy) | `[ASSUMPTION]`. |

---

## F. Award, Approval Thresholds & Constraints

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-071** | An award recommendation may be recorded only after evaluation is `Finalized` and the recommended proposal passes all thresholds. | Sound basis for award. | Domain + API | — |
| **BRULE-072** | Award approval routing is determined by an **approval-authority matrix** keyed on award **amount bands**; the default is a **single approver**, configurable to multi-level. | Proportional control (discovery §5). | API (config) + Domain | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` bands below are placeholders. |
| **BRULE-073** | **Segregation of duties**: the award approver must **differ** from the recommender. | Anti-collusion control. | API (policy) | `[ASSUMPTION]`. |
| **BRULE-074** | An approver may approve only awards **within their authority limit**; over-limit awards escalate to the next level. | Enforce spending authority. | Domain + API | Bands `[ASSUMPTION]`. |
| **BRULE-075** | The winning supplier must still be **`Active`** at the moment of approval; suspension/expiry blocks the award. | Prevent awarding to non-compliant suppliers. | Domain + API | Ties to BRULE-006/023. |
| **BRULE-076** | Rejecting an award **requires a reason** and returns the RFQ to `Recommendation` for rework or alternate selection. | Traceable rejection + recovery path. | Domain + API | — |
| **BRULE-077** | Award is **final within the portal** upon `Awarded`, independent of ERP availability; the ERP Purchase Order is created **asynchronously** via Outbox and is eventually consistent. | Canonical §1 (portal never blocks on ERP). | Domain + System | — |
| **BRULE-078** | The award emits a transactional Outbox `AwardApproved` event in the **same transaction** as the state change; ERP PO creation is retried with backoff on failure. | Exactly-effectively-once integration. | System (Outbox/ACL) | — |
| **BRULE-079** | `ExternalPurchaseOrderRef` (string) is stored when the ERP acknowledges; the RFQ then moves to `Completed`. | ERP string-id boundary (canonical §1). | Domain + System | — |
| **BRULE-080** | At most **one winning award** per RFQ line-scope; multi-line/split awards (if allowed) follow a defined split policy. | Prevent double-award. | Domain | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` split-award support. |
| **BRULE-081** | If the winner `Declines`, the RFQ returns to `Recommendation` to award the next-ranked eligible proposal. | Continuity of procurement. | Domain + API | — |
| **BRULE-082** | Losing suppliers receive a **regret notification** at award; commercial details of the winner are **not** disclosed to them. | Fairness + confidentiality. | System + API | Disclosure policy `[ASSUMPTION]`. |
| **BRULE-083** | Award decisions, approvals, and their justifications are **immutable** post-`Awarded` and fully audited. | Governance and dispute defense. | Domain | — |

### F.1 Illustrative approval-authority matrix `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`

> Placeholder amounts only — **not** real policy; awaiting business confirmation. Currency default SYP.

| Band | Award amount (SYP) | Required approval level(s) |
|---|---|---|
| 1 | ≤ threshold-A | `procurement_manager` (single) |
| 2 | threshold-A .. threshold-B | `procurement_manager` + senior approver |
| 3 | > threshold-B | Multi-level + governance sign-off |

---

## G. Data Visibility, Scoping & Confidentiality

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-084** | Suppliers see **only** data scoped to their own `SupplierId` (their profile, invitations, proposals, awards). | RBAC row-scoping (canonical §6). | API (policy) | Enforced server-side, not UI. |
| **BRULE-085** | Procurement roles and evaluators see data scoped to their **`OrganizationId`**; cross-organization access is denied. | Tenancy isolation. | API (policy) | — |
| **BRULE-086** | The **Ministry** persona has **read-only, cross-organization** access to **aggregate/governance** metrics only. | Canonical §6 governance scope. | API (policy) | — |
| **BRULE-087** | Whether the Ministry may view **commercial values** (prices) or only **anonymized/aggregate** metrics is a policy decision; default is aggregate-only. | Discovery open question §5. | API (policy) | `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]`. |
| **BRULE-088** | No supplier can see the **existence, content, pricing, or identity** of any competing supplier's proposal, at any lifecycle stage. | Bid confidentiality. | API (row-scope) + Domain | — |
| **BRULE-089** | Evaluator identities and individual scores are **not** disclosed to suppliers; only final outcomes are communicated. | Protect committee independence. | API + System | `[ASSUMPTION]`. |
| **BRULE-090** | Sensitive PII/financial fields (bank accounts, contact PII) are **masked** by default and require explicit permission to reveal; reveals are audited. | OWASP ASVS L2; least privilege. | API + UI | — |
| **BRULE-091** | Personal/sensitive data is **never** placed in URLs, query strings, logs, or notification payloads. | Privacy by design (canonical §9). | API + System | — |
| **BRULE-092** | `system_admin` has global technical access but administrative reads of sensitive records are **audited**; admin cannot silently alter procurement outcomes. | Prevent privilege abuse. | API + Domain | — |
| **BRULE-093** | Draft RFQs and in-progress evaluations are visible only to the owning Organization's authorized roles, never to suppliers or other orgs. | Pre-publication confidentiality. | API (policy) | — |
| **BRULE-094** | All list/detail responses are filtered by permission **and** scope at the API; the UI hides affordances but is never the security boundary. | Defense in depth (canonical §6). | API + UI | — |

---

## H. Audit, Notifications & Integrity (cross-cutting)

| Rule | Statement | Rationale | Enforcement | Notes |
|---|---|---|---|---|
| **BRULE-095** | Every state transition writes an immutable **AuditLog** entry `(actor, timestamp, from→to, reason?, correlationId)`. | Canonical §5 auditability; OWASP ASVS L2. | Domain + Infra | — |
| **BRULE-096** | Every **reject / cancel / suspend / deactivate / info-request / recuse** action requires a **mandatory reason**, persisted and surfaced. | Actionable, defensible decisions. | Domain + API | — |
| **BRULE-097** | Illegal state transitions are **rejected by the domain**, returning a typed error; they are never merely hidden in the UI. | Canonical §5 integrity guarantee. | Domain | — |
| **BRULE-098** | Concurrency conflicts are detected via **`RowVersion`** optimistic checks; the second writer is rejected with a conflict, not silently overwritten. | Data integrity under concurrency. | Domain + Infra | — |
| **BRULE-099** | Outbound notifications (email/in-app) are delivered via the durable **Outbox/queue**; notification failures never roll back the committed domain change. | Reliability without coupling. | System | — |
| **BRULE-100** | All timestamps are stored in **UTC** and rendered locale-aware (Gregorian default, Arabic-first, RTL); deadlines compare in UTC server-side. | Canonical §8 localization + BRULE-034 deadline integrity. | Domain + UI | Hijri display `[ASSUMPTION]` future. |

---

## Traceability

| Rule area | Canonical source | Process source |
|---|---|---|
| A. Eligibility | §5 onboarding, §1 ExternalId, §8 localization | [BUSINESS-PROCESSES §1](BUSINESS-PROCESSES.md#1-supplier-onboarding) |
| B. Documents | §5 document machine | [BUSINESS-PROCESSES §2](BUSINESS-PROCESSES.md#2-supplier-document-lifecycle) |
| C. RFQ | §5 RFQ machine, §4 identifiers | [BUSINESS-PROCESSES §3](BUSINESS-PROCESSES.md#3-rfq-request-for-quotation) |
| D. Proposals | §5 proposal machine | [BUSINESS-PROCESSES §4](BUSINESS-PROCESSES.md#4-proposal) |
| E. Evaluation | §5 evaluation machine (independence) | [BUSINESS-PROCESSES §5](BUSINESS-PROCESSES.md#5-evaluation) |
| F. Award | §5 award machine, §1 ERP boundary | [BUSINESS-PROCESSES §6](BUSINESS-PROCESSES.md#6-award--approval) |
| G. Visibility | §6 RBAC & scoping | cross-cutting |
| H. Audit/integrity | §5, §9 NFRs | [BUSINESS-PROCESSES §8](BUSINESS-PROCESSES.md#8-cross-process-invariants) |

> **Consistency note:** all `[ASSUMPTION / REQUIRES BUSINESS CONFIRMATION]` items above are candidates for
> [`ASSUMPTIONS.md`](ASSUMPTIONS.md) / [`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md). No Syrian legal, tax, or
> regulatory rule has been invented; such fields are captured generically pending confirmation.
