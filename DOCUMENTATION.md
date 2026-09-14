# MOTS Supplier Portal

Complete project documentation. Compiled 14 September 2026 from the specification set in `docs/`,
the decision log, and the code as it stands today.

This document describes the product: what it is for, who uses it, what it does, how it is built, and
what remains open. It is written to be read start to finish by someone who has never seen the system,
and to be searched later by someone who needs one specific answer.

---

## Contents

1. [Summary](#1-summary)
2. [The problem](#2-the-problem)
3. [Who uses the portal](#3-who-uses-the-portal)
4. [What the portal does](#4-what-the-portal-does)
5. [The rules that govern it](#5-the-rules-that-govern-it)
6. [Access control](#6-access-control)
7. [The screens](#7-the-screens)
8. [Architecture](#8-architecture)
9. [The domain model](#9-the-domain-model)
10. [Data and identifiers](#10-data-and-identifiers)
11. [The API](#11-the-api)
12. [Security](#12-security)
13. [Arabic, accessibility, and the design system](#13-arabic-accessibility-and-the-design-system)
14. [Integration with ERPNext](#14-integration-with-erpnext)
15. [Running the system](#15-running-the-system)
16. [How correctness is checked](#16-how-correctness-is-checked)
17. [Boundaries and open decisions](#17-boundaries-and-open-decisions)
18. [Glossary](#18-glossary)

---

## 1. Summary

The MOTS Supplier Portal is a procurement platform for the Syrian tourism sector. It gives suppliers,
hotels, Ministry-affiliated buying bodies, and the Ministry of Tourism itself one place to run the
whole sourcing chain: a supplier registers and is verified once, buying entities publish requests for
quotation and invite suppliers, suppliers submit priced proposals, committees score those proposals
against weighted criteria, and an award is approved and recorded with a full audit trail.

Four things define it.

**Arabic first.** Arabic is the default language and right-to-left is the default direction. English
is the secondary language. This is not a translation layer added at the end; layout uses CSS logical
properties throughout, and every screen is verified in both directions.

**Auditable by construction.** Every state change records who did it, when, what changed from and to,
why, and a correlation identifier that ties it to the request and to the background work that
followed. The audit log cannot be edited or deleted by anyone, including administrators.

**Fair by construction.** Evaluation criteria are weighted and fixed before proposals arrive, the
template is snapshotted onto the RFQ so later edits cannot alter a live competition, evaluators score
without seeing each other's scores, and submission deadlines are enforced by the domain rather than by
the interface.

**Standalone, but built for the ERP.** The portal runs and delivers its full value with ERPNext
switched off. Every entity the ERP will eventually own carries a nullable string identifier, and all
ERP traffic goes through a translation layer and a transactional outbox. Nothing a user does waits on
the ERP.

The system is built on .NET 10 and PostgreSQL 17 on the server, React 19 on the client. It runs as
three container images (API, background worker, web) plus a database and object storage.

---

## 2. The problem

Procurement in the sector runs on email, paper, WhatsApp, and phone calls. The costs are concrete.

| What happens today | Who pays for it |
|---|---|
| Suppliers resend the same company documents to every hotel they deal with | Suppliers duplicate effort; buyers circulate stale and expired documents |
| RFQs go out ad hoc, by whatever channel is to hand | Invitations get missed, scope varies between recipients, deadlines are unenforceable |
| Quotes arrive as PDFs, images, and verbal prices | Someone re-keys them by hand, and comparison is guesswork |
| Evaluation is informal and undocumented | Decisions cannot be reproduced or defended, and bias is impossible to disprove |
| The reasons for an award live in one person's inbox | Disputes have nothing to resolve against |
| Nobody has a sector-wide view | The Ministry cannot see participation, competition, or spend |
| Nothing is Arabic-first or works on a phone | Smaller suppliers are effectively excluded |

The result is slow cycles, low trust, and decisions that cannot be defended after the fact. No system
owns the chain from registration through award as one auditable flow. That chain is what the portal
owns.

### What success looks like

The measure the product is steered by is the share of RFQs that reach a fully digital, audited award:
invitation, submission, evaluation, and approval all inside the portal with a complete trail, as a
percentage of all RFQs published. It moves only when every link in the chain is genuinely adopted.

Supporting measures include verified suppliers onboarded, median time from submission to approval,
compliant proposals per RFQ, invitation-to-proposal conversion, median cycle time from publication to
award, the share of awards with a complete recommendation and approval trail, and document validity
across the supplier base. Numeric targets are set with the business once a first quarter of live usage
provides a baseline.

---

## 3. Who uses the portal

Eight roles exist in the system. Each is a real job, and each sees a different product.

### Suppliers

**`supplier_admin`** is the primary representative of a supplier company. They register the company,
complete the onboarding profile, upload compliance documents, respond to invitations, submit and
withdraw proposals, manage bank accounts, and invite colleagues. They work from a phone as often as a
desktop.

**`supplier_user`** is a delegated colleague. They can edit the profile and work on proposals, but
cannot submit a proposal, withdraw one, manage bank accounts, or invite other users. Delegation
without sharing credentials is the point.

A supplier uses the product a few times a year, usually against a deadline, and often will not have
used it since the last time. Nothing assumes familiarity.

### Buying entities

**`procurement_officer`** authors RFQs, adds line items and requirements, invites suppliers, answers
clarifications, closes submission, opens evaluation, consolidates scores, and drafts the award
recommendation. This is the person who lives in the product all day.

**`procurement_manager`** approves RFQs for publication, assigns evaluators, finalises evaluation,
approves or rejects awards, cancels RFQs, reassigns ownership, and can shorten a submission deadline.
Their permissions are the governance layer over the officer's work.

**`evaluator`** scores proposals against the criteria and submits those scores. They can request
clarification on a proposal and declare a conflict of interest. They see nothing else.

### Compliance

**`onboarding_reviewer`** picks up supplier applications, reviews profile sections and documents,
approves or rejects them with reasons, requests more information, and manages the post-approval
lifecycle (suspending and reinstating suppliers).

### Oversight

**`ministry_viewer`** has read-only access across every organisation: governance dashboards, the
supplier registry, tenders across all buying bodies, award analytics, category coverage, and
procurement and compliance reports. This role cannot change anything.

### Platform

**`system_admin`** manages users, roles and their permissions, organisations, reference data,
evaluation templates, system settings, notification and email templates, interface string overrides,
and the operational consoles. It holds every permission in the system.

---

## 4. What the portal does

### 4.1 Registration and onboarding

A prospective supplier registers with a company name, a representative name, an email address, a
phone number, and a password. The system creates the supplier in `Draft` and sends a verification
email. Verifying moves it to `EmailVerified`.

From there the supplier completes a profile that is deliberately a superset of what the ERP will
eventually want: legal information (legal name in Arabic and English, registration number, tax
identifier, supplier type, incorporation date), addresses, contacts, representatives, branches, bank
accounts, the categories of goods and services they supply, and their offerings catalogue.

Legal, registration, and tax fields are captured generically. The portal does not invent Syrian
regulatory validation it cannot verify, and every such field is flagged for business confirmation
rather than guessed at.

Documents are uploaded against configurable document types. Each document moves through its own
lifecycle: `Required`, `Uploaded`, `UnderReview`, then `Approved` or `Rejected` with a reason. An
approved document with an expiry date later becomes `ExpiringSoon` and then `Expired`, driven by a
scheduled job. A rejected or expired required document flags the profile as incomplete.

The application cannot be submitted until every required document is present, at least one category is
linked, a head-office address exists, and legal information is valid. A completeness checklist shows
the supplier exactly what is missing.

Submission moves the application to `Submitted` and makes it read-only to the supplier. A reviewer
claims it (`UnderReview`) and can approve it, reject it with a mandatory reason, or request more
information. Requesting information returns control to the supplier, who addresses the feedback and
resubmits. That loop can repeat as many times as needed and every turn of it is audited.

Approval moves the supplier to `Approved` and into the `Active` lifecycle, and queues an outbox event
that will create the supplier master in the ERP when integration is switched on. After approval a
supplier can be suspended (reversibly, with a reason) and eventually deactivated. Suspended and
deactivated suppliers cannot be invited to new RFQs or submit proposals.

### 4.2 Authoring and publishing a tender

A procurement officer creates an RFQ in `Draft` with a title, description, the buying organisation,
a currency, and a timeline covering publication, the submission window, the clarification deadline,
and a target award date.

To that they add line items (title, category, quantity, unit of measure, specification) and
requirements, which are the qualifying conditions a supplier must satisfy. They attach specification
documents. They bind an evaluation template, and that template is snapshotted onto the RFQ at the
moment of binding, so later edits to the template cannot change the criteria of a competition already
under way.

The RFQ goes for internal review. A manager approves it or returns it for edits with comments.
Publication is a separate, permission-guarded action. After publication the RFQ is locked except for
addenda, which announce a change and notify every invited supplier.

Officers invite specific active suppliers. Each invitation is tracked through its own statuses, and
the system can suggest candidates by matching supplier categories and offerings against the RFQ's line
items. Only invited suppliers can see the RFQ or submit against it.

The submission window opens and closes on the timeline, driven by scheduled jobs. A buyer can close
early with a reason, and a manager can shorten a deadline under a permission of its own. Late
submissions are refused by the domain, not merely hidden by the interface.

Throughout the clarification window, suppliers ask questions and the officer answers them. Answers can
be private to the asker or published to everyone invited, with the asker anonymised.

### 4.3 Proposals

An invited supplier starts exactly one proposal per RFQ. They price each line item, capture commercial
terms (payment terms, delivery and lead time, incoterm, validity period), write a technical response
against the requirements, and attach supporting documents.

Drafts persist and are private to the supplier. Nothing is visible to the buyer until submission, and
the totals are always recomputed by the server from the line items rather than accepted from the
client.

Submission is only possible while the window is open, and only after every mandatory item is priced
and every required response and document is present. A supplier can withdraw a submitted proposal
while the window is still open, with a reason.

After submission the buyer may request clarification, which returns the proposal to the supplier for
revision and then back for review. That loop is repeatable and audited.

Proposal attachments carry an envelope marking, commercial or technical, so that a two-envelope
process can be honoured where it is required.

### 4.4 Evaluation

A manager assigns evaluators. Each evaluator opens their assignment and scores every proposal against
every criterion in the RFQ's snapshotted template, with optional comments per criterion. Weighted
totals are computed by the domain and by nothing else.

Evaluators score independently. An evaluator cannot see another evaluator's scores or comments before
submitting their own. Bidders are anonymous during scoring and revealed after consolidation. An
evaluator can declare a conflict of interest and be recused.

Submitting locks an evaluator's scores. Reopening them is possible only under a separate permission
and is audited.

Once all assigned evaluators have submitted, results are consolidated into a ranked, weighted result
per proposal, with threshold pass or fail flags. Criteria can carry thresholds, and a proposal failing
one is flagged and excluded from award eligibility unless someone overrides that, which is itself
audited.

A genuine tie is surfaced rather than broken. The system applies the documented first tie-break rung
to make ranking deterministic, and where a full tie remains it refuses to pick a winner and puts the
case in front of a person. That refusal is a deliberate design decision, recorded as such.

Finalising the evaluation unlocks shortlisting and recommendation on the RFQ.

### 4.5 Comparison and award

The comparison view puts every submitted proposal side by side: line prices, totals, commercial terms,
technical responses, and evaluation scores with ranking. It highlights the best price per line and
flags threshold failures. It respects blindness rules until the evaluation is finalised, and it can be
exported for the award file.

The officer writes a recommendation, naming the winning proposal and justifying it against the
consolidated result. That recommendation routes for approval. Every award takes the full approval
path; there is no value threshold below which approval is skipped.

An approver approves or rejects with a mandatory reason. Rejection returns the recommendation with
feedback. Approval issues the award: the winning proposal moves to awarded, the others to not
selected, every supplier is notified, and an outbox event is queued that the integration layer will
translate into an ERP purchase order. The award completes whether or not the ERP is reachable.

The outcome, the justification, and a snapshot of the comparison are retained as the award file.

### 4.6 Oversight and reporting

The Ministry sees cross-organisation dashboards: a governance overview, every tender across every
buying body, the detail of one tender including each named bidder, the supplier registry as an
overseer reads it, award analytics covering spend by month, category and buying body, and category
coverage of approved against active suppliers.

Whether the Ministry sees commercial figures is configurable and currently gated. The standing
default under-discloses: aggregate metrics rather than commercial values, unless a decision says
otherwise for real data.

Procurement and compliance reports are available to managers and the Ministry, and both are
exportable.

### 4.7 Notifications

The system notifies on invitations, submissions, clarifications, decisions, deadline changes, document
expiry, and award outcomes. Notifications go in-app and by email, in the recipient's own language,
from templates an administrator can override. An in-app centre keeps the history with read state and
deep links.

Notification generation is decoupled from the transaction that caused it through the outbox, so an
email outage cannot block a domain action. Users can opt out of informational notification types only.

### 4.8 Administration

Administrators manage users and staff invitations, roles and the permissions attached to them,
organisations and their units, and the many-to-many links between suppliers and buying entities.

They maintain reference data: the category tree, document types, currencies, units of measure,
incoterms, and regions. Reference data is deactivated rather than deleted, and codes are immutable
once issued.

They manage evaluation templates, system settings, notification and email templates, and interface
string overrides. Operational consoles cover outbox health with message replay, ERP sync status,
background job health with the ability to trigger a recurring job, storage settings, and security
posture.

---

## 5. The rules that govern it

Five state machines define what the product will and will not allow. They live in the domain layer.
An illegal transition is refused by the server with a typed error; the interface merely avoids
offering it.

### Supplier onboarding

```
Draft → EmailVerified → ProfileInProgress → Submitted → UnderReview
        → (InfoRequested → Resubmitted → UnderReview)*
        → Approved | Rejected

after approval:  Active ↔ Suspended → Deactivated
```

### Documents

```
Required → Uploaded → UnderReview → Approved | Rejected(reason)
Approved → ExpiringSoon → Expired          (time-driven)
```

Rejected or expired required documents flag the profile incomplete. Expiry of a document marked
award-critical suspends the supplier automatically. That mechanism is complete and tested, but every
seeded document type currently has the flag off, so it suspends nobody until the Ministry says which
types are award-critical.

### RFQ

```
Draft → InternalReview → Approved → Published → SubmissionOpen → SubmissionClosed
      → UnderEvaluation → Clarification* → Shortlisting → Recommendation
      → AwardApproval → Awarded → Completed

Cancelled: reachable from any state before Awarded, with a reason
```

### Proposal

```
Draft → Submitted → UnderReview
      → (ClarificationRequested → Revised → UnderReview)*
      → Shortlisted | NotSelected → AwardOffered → Awarded | Declined

Withdrawn: supplier-initiated, while submission is open
```

### Evaluation

```
NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated → Finalized
```

### Award

```
Recommended → PendingApproval → Approved | Rejected → Awarded → (outbox → ERP purchase order)
```

### Decisions taken during construction

Some rules were not settled by any specification and had to be decided in order to build. They are
recorded in `DECISIONS-TAKEN.md` with the reasoning, so a later reader can overturn one knowingly
rather than discover it by surprise. The principle behind all of them: a default under-serves rather
than over-discloses, and where a choice would produce an outcome rather than a posture (who wins, a
tie-break, an approval threshold) the system decides nothing and puts the case to a person.

Examples of what that produced: the Ministry sees no commercial figures by default; every award takes
the full approval path; a tie is surfaced rather than broken; there is no cooldown on re-application
after rejection; deadline extension is unbounded but always audited; an award offer has no acceptance
window and the supplier cannot accept it; malware scanning fails closed and scans on first access.

---

## 6. Access control

Authorisation is deny by default. Every protected endpoint declares the permission it requires, in the
form `resource.action`. The interface hides what a user cannot do, but that is only an affordance; the
server re-checks every permission on every request and never trusts the client.

The system defines **53 permissions across 8 roles**. The full catalogue is generated from the code
itself into `PERMISSIONS.md` by a test that fails when the file and the code disagree, so the document
cannot drift from reality.

| Role | What it can do |
|---|---|
| `supplier_admin` | Create, edit, submit, revise, withdraw and decline proposals; edit the supplier profile; manage bank accounts; submit the application; manage supplier users; read RFQs |
| `supplier_user` | Create and edit proposals; edit the supplier profile; read RFQs |
| `onboarding_reviewer` | Review, approve, reject and request information on applications; review documents; manage supplier lifecycle |
| `procurement_officer` | Create, edit, publish and close RFQs; invite suppliers; issue addenda; answer clarifications; open and consolidate evaluation; recommend awards; view comparisons; read the supplier directory; search offerings |
| `procurement_manager` | Approve, cancel, reassign and review RFQs; shorten deadlines; assign evaluators; finalise and reopen evaluation; manage evaluation templates; approve and reject awards; read reports |
| `evaluator` | Score and submit scores; request clarification on a proposal |
| `ministry_viewer` | Read governance data and reports across all organisations |
| `system_admin` | Everything |

### Row scoping

Permissions say what an action is; row scoping says whose data it touches. Each user belongs to
exactly one scope:

- a supplier user is bound to a `SupplierId` and can only ever see that supplier's data;
- a back-office user is bound to an `OrganizationId` (and optionally an organisational unit);
- the Ministry is read-only across all organisations;
- a platform administrator is global.

Scoping is enforced on the server. A supplier cannot reach another supplier's proposal by guessing a
URL, because identifiers in URLs are opaque reference codes rather than database keys, and because the
query is scoped before it runs.

### Audit

Every state change writes an audit record with the actor, the timestamp, the transition from and to,
the reason, and a correlation identifier. The log is append-only: no update or delete path exists for
anyone. Document views and downloads, and data exports, are recorded as access events. Caller IP is
truncated to a /24 for IPv4 and /48 for IPv6, because retention is not yet decided and a full address
kept indefinitely is the most exposed and least reversible form of that data.

---

## 7. The screens

The React application declares **70 pages** across two shells and a set of public routes. The count is
parsed from the router by the accessibility suite, so a page added without reaching that suite is
impossible.

### Public

`/login`, `/register`, `/forgot-password`, `/reset-password`, `/verify-email`, `/accept-invite`,
`/accept-staff-invite`, `/about`, `/help`

### Supplier

`/dashboard`, `/onboarding` with sections for addresses, banking, contacts and offerings, `/profile`,
`/documents`, `/offerings`, `/rfqs` and one tender at `/rfqs/$referenceCode`, the proposal editor at
`/rfqs/$referenceCode/proposal`, `/proposals`, `/team`, `/settings` and `/settings/notifications`,
`/notifications`

### Back office

The tender workspace is the centre of the buyer's day: `/rfqs/$referenceCode` with tabs for suppliers,
proposals, comparison, award and settings, plus the evaluator's own views at `/my-evaluation` and
`/brief`.

Around it: `/dashboard`, `/rfqs`, `/procurement` and `/procurement/approvals`, `/review`,
`/review/$referenceCode`, `/review/suppliers`, `/review-dashboard`, `/evaluation`,
`/evaluation-templates`, `/suppliers`, `/offerings`, `/reports`, `/search`, `/notifications`,
`/account` and `/account/notifications`, `/help`

### Ministry

`/ministry`, `/ministry/rfqs`, `/ministry/rfqs/$referenceCode`, `/ministry/suppliers`,
`/ministry/awards`, `/ministry/categories`

### Administration

`/admin`, `/organizations`, `/staff`, `/roles`, `/reference`, `/settings`, `/notification-templates`,
`/email-templates`, `/ui-strings`, `/operations`, `/audit`

---

## 8. Architecture

### Principles

The rules below shape every decision downstream. Their purpose is to keep business logic pure,
testable, and independent of the framework, the database, the ERP, and the user interface.

1. The domain is the centre. Business rules depend on nothing external: no Entity Framework, no
   ASP.NET, no HTTP, no ERP types.
2. Dependencies point inward. `Api → Application → Domain`, and `Infrastructure → Application/Domain`.
   Inner layers never reference outer ones, and a test suite fails the build if they do.
3. Features are vertical slices. One feature is an endpoint, a command or query, a validator, a
   handler, persistence, and tests. There are no god-services and no generic repositories.
4. The portal is ERP-independent. All ERP interaction is asynchronous through a translation layer and
   a transactional outbox.
5. Explicit over implicit. Handlers are resolved directly from the container rather than through a
   mediator, mapping is source-generated, and transitions are written out.
6. Illegal states are unrepresentable. The domain rejects illegal transitions with a typed error.
7. Secure and auditable by default, on every state change.
8. Arabic, accessibility, and the visual quality bar are architectural constraints, not a later pass.
9. Public identifiers are opaque. Internal keys never appear in a URL.
10. Everything is observable, with correlation carried from the browser through to a background job.

### Containers

| Container | Responsibility |
|---|---|
| React single-page application | Every persona's interface. Renders affordances by permission, validates with Zod, handles right-to-left and translation |
| Reverse proxy | TLS, static assets, `/api` routing, compression, security headers |
| .NET API | Authentication, validation, command and query handlers, domain execution, persistence. Writes the outbox message in the same transaction as the state change. Stateless, so it scales horizontally |
| Background worker | Hangfire: outbox dispatch, notifications, document expiry sweeps, reminders, ERP sync. Durable retries and dead-lettering |
| PostgreSQL 17 | Every portal-owned record, plus the outbox, the audit log, and Hangfire's own storage |
| Object storage | Documents, behind a storage abstraction. Local disk in development, S3-compatible in production |
| Translation layer and adapters | The only code that knows ERPNext exists |

### Layers inside the API

| Layer | Owns | May depend on |
|---|---|---|
| Domain | Aggregates, entities, value objects, invariants, state machines, domain events | Nothing but the base class library |
| Application | Commands, queries, handlers, validators, ports (interfaces), DTOs, mapping | Domain |
| Infrastructure | Persistence, migrations, outbox, file storage, ERP adapters, email, identity, jobs | Application, Domain |
| Api | Endpoint groups, the request pipeline, OpenAPI, dependency wiring | Application, Infrastructure, Domain |

The load-bearing detail is at the ERP boundary. The application layer declares a gateway interface;
infrastructure provides the ERPNext-specific adapter. The domain and application layers compile and
test with no knowledge that ERPNext exists.

### A request, end to end

Submitting a proposal shows the shape every feature follows. The browser sends a JWT and a correlation
identifier. The pipeline authorises `proposal.submit` scoped to the caller's supplier, then validates
the payload. The handler calls `Submit()` on the proposal aggregate, which guards the transition from
draft to submitted and either raises a domain event or returns a typed error. The handler persists the
aggregate, the audit record, and the outbox message in one transaction. Later, out of band, the worker
claims the outbox row and sends what follows.

Authorisation happens before work, the domain guards the transition, and the state change, the audit
entry, and the outbox message commit atomically. An asynchronous side effect can never be lost or
fired twice without a durable record of it.

### Technology

Server: .NET 10 and C# 14, ASP.NET Core Minimal APIs, Entity Framework Core 10 with Npgsql against
PostgreSQL 17, FluentValidation, ASP.NET Core Identity with JWT access tokens and rotating refresh
tokens, Hangfire on PostgreSQL storage, Serilog writing structured JSON, OpenTelemetry with a
Prometheus exporter, MailKit for email, AWS SDK for S3-compatible storage, SkiaSharp and HarfBuzz for
export rendering (Arabic shaping in generated PDFs needs real text shaping).

Client: React 19 and TypeScript, Vite, TanStack Router and TanStack Query, TanStack Table, Zustand for
the small amount of client state, React Hook Form with Zod, Tailwind CSS v4 with Radix primitives,
i18next, Recharts, Lucide icons, IBM Plex Sans Arabic and Inter self-hosted.

Testing: xUnit, FluentAssertions, Testcontainers, NetArchTest, Vitest, Testing Library, Playwright,
axe-core, Storybook.

Two notable absences. There is no mediator library, because a thin dispatcher removes both the
indirection and a commercial licence. Mapping is source-generated at compile time rather than resolved
by reflection.

---

## 9. The domain model

Aggregates are the transactional consistency boundary. One aggregate is loaded, changed, and saved per
command. References across boundaries are held by identity, never by object reference: a proposal
holds a supplier identifier, not a supplier object graph. Work that crosses aggregates goes through
domain events and the outbox, so awarding a proposal does not reach into the supplier aggregate; it
raises an event that something else reacts to.

| Aggregate | Contains | Synced to ERP | Reference prefix |
|---|---|---|---|
| User | Role assignments, sessions, MFA enrolment | No | — |
| Organization | Organisational units | To ERP company | `ORG` |
| Supplier | Profile, legal information, addresses, contacts, representatives, branches, bank accounts, category links, offerings, documents, onboarding state | Yes | `SUP` |
| RFQ | Items, requirements, attachments, invitations, clarifications, timeline, template reference | Yes | `RFQ` |
| Proposal | Items, documents, commercial terms, technical response, validity, totals | Yes | `PRO` |
| EvaluationTemplate | Criteria | No | `EVT` |
| Evaluation | Assignments, evaluator scores, consolidated result | Result feeds the ERP | `EVL` |
| Award | Recommendation, approvals, decision, purchase order reference | Yes, to a purchase order | `AWD` |
| Notification, AuditLog, Document, OutboxMessage, Category | — | No | — |

### Invariants worth knowing

A supplier has exactly one primary representative at all times, and exactly one default bank account
when any exist. A proposal is unique per supplier per RFQ. An evaluation template's criteria weights
must sum to 100 before it can be activated, and a template referenced by a live RFQ is immutable;
editing it produces a new version. An award may only reference proposals that were shortlisted and
passed their thresholds, and reaching awarded requires the whole approval chain resolved.

### Shared value objects

`Money` (amount and ISO currency, defaulting to SYP, arithmetic only within one currency),
`Quantity`, `DateRange`, `Address`, `ContactInfo`, `LegalInfo`, `BankAccountInfo`, `LocalizedText`
(Arabic required, English optional), and `ExternalSyncInfo` for anything the ERP will touch. A value
object validates at construction, so an invalid one cannot exist.

---

## 10. Data and identifiers

Three identifier concepts exist and are never conflated.

**Internal primary keys** are version 7 UUIDs, generated by the application before insert. The leading
bits are a millisecond timestamp, so keys are time-ordered and index-friendly rather than scattering
writes across the index the way random UUIDs do. They are never exposed in a URL or an API response
intended for a user.

**Public reference codes** are what people cite: `RFQ-2026-000123`, `SUP-2026-000078`,
`PRO-2026-004510`, `AWD-2026-000045`. They are allocated from a per-prefix, per-year counter. Where
enumeration must be prevented entirely, a short opaque code is used instead.

**ERP external identifiers** are nullable strings matching ERPNext's naming series, and they stay null
until the ERP acknowledges the entity. They are never integer foreign keys, because ERPNext keys are
strings and an integer key would couple the portal's schema to the ERP's.

Concurrency is optimistic. Each aggregate root carries a version that guards the whole aggregate
against lost updates, and conflicting writes are refused with a typed conflict rather than silently
overwriting. Creating a child of a versioned aggregate counts as a mutation of that aggregate and is
guarded the same way. On the wire this surfaces as ETags and `If-Match` preconditions; state
transitions require them.

Offerings carry a typed core plus a flexible attribute bag stored as JSONB, because the useful
attributes of a catering offering and a transport offering are not the same.

Schema changes are versioned EF Core migrations applied as an explicit pipeline step, never
implicitly on application start in production. Five migrations exist today, the first of which creates the whole schema.

---

## 11. The API

The API is REST over HTTPS with JSON, about **234 routes** grouped by feature across the endpoint
files. It is documented by the OpenAPI document the build generates, and that document is the
deliverable: a committed baseline is compared against the generated one by a contract test, so an
endpoint cannot change shape unnoticed.

Conventions worth knowing:

- List endpoints return a `{ data, pagination, meta }` envelope and are paginated server-side, with
  keyset cursors rather than offsets.
- Errors are RFC 9457 problem documents, carrying machine-readable codes rather than prose, so the
  client can translate them.
- Writes that change state require an `If-Match` precondition carrying the current ETag, and answer
  `412` when the version has moved.
- Partial updates use RFC 7396 merge patch semantics.
- Idempotency keys are accepted where a double submit is plausible, and reserved by a unique index so
  that an in-flight key is a conflict rather than a duplicate.
- A locked account answers `423`, not `429`.
- Registration responses are deliberately identical whether or not the email already exists, so the
  endpoint cannot be used to enumerate accounts.

Internal identifiers still appear in some payloads alongside reference codes. Removing them is a
breaking change deferred to a future major version rather than made quietly.

---

## 12. Security

The target is OWASP ASVS Level 2. The formal review against it is deferred to a later pass, which is
recorded rather than implied.

**Authentication** uses ASP.NET Core Identity with JWT access tokens and rotating refresh tokens.
Reusing a revoked refresh token invalidates the whole token family and forces a fresh login. Password
policy covers length, complexity, and a breached-password check, with lockout and backoff after
repeated failures. Time-limited single-use tokens handle email verification and password reset, and a
reset invalidates active sessions. Users can see their active sessions and sign out of one or all
devices. TOTP two-factor authentication is available and can be required by role.

**Authorisation** is policy-based on permission claims, deny by default, with row scoping enforced
server-side as described in section 6.

**Files** are validated for type, MIME and size, malware-scanned before acceptance, and stored outside
the web root. Scanning fails closed: a file that cannot be scanned is not accepted. Access is through
time-limited signed URLs, and every grant is audited.

**Transport and headers**: TLS only, HSTS, a content security policy, and the usual protective
headers. Authenticated reads forbid cache reuse without revalidation, which is stricter than the
security specification asks for.

**Secrets** live in the environment or a secret store, never in source, images, or logs. CI scans
dependencies and fails the build on a high or critical advisory, and that gate is itself tested with a
deliberately vulnerable canary package to prove it still bites.

**Data protection**: personal and commercial data is scoped by role, suppliers never see each other's
commercial data, sensitive values never appear in URLs or logs, and bank account numbers are encrypted
at rest and revealed only through an audited action.

---

## 13. Arabic, accessibility, and the design system

### Language and direction

Arabic is the default and right-to-left is the default direction; English is secondary. Every string
is keyed rather than hard-coded. Layout uses CSS logical properties throughout, so direction is a
document attribute rather than a set of overrides, and directional icons mirror.

Arabic renders Eastern Arabic digits and English renders Western digits, both pinned explicitly rather
than left to the browser's locale data, because ICU builds disagree about what bare `ar` should
produce and a deadline should not read differently on two machines.

Dates and times are formatted in Asia/Damascus with the zone pinned, so a deadline of 23:00 Damascus
does not display as 20:00 to a viewer whose laptop is on UTC. Storage is UTC throughout.

Default currency is SYP. Proposals can carry other currencies with a display currency for comparison.

The Arabic copy was reviewed and accepted as a body of work, including deliberate word choices that a
translator might otherwise have varied.

### Accessibility

The target is WCAG 2.2 AA. What is enforced automatically today: axe-core runs against all 70
application routes in both languages, and against every Storybook story; keyboard operability,
focus order, and the absence of keyboard traps are tested with real key presses rather than synthetic
clicks; error messages are programmatically associated with their fields and that association is
verified in the rendered DOM rather than assumed; required fields are announced to assistive
technology and not only marked with an asterisk; reflow at 320 pixels is measured on every back-office
route; and `prefers-reduced-motion` is proven to change rendered behaviour rather than merely appear
in a stylesheet.

Colour contrast is computed against the tokens rather than eyeballed, and several token values carry a
comment recording the measured ratio that forced them.

A full manual WCAG audit is deferred alongside the security review.

### The design system

The visual language is bespoke rather than a component kit: an evergreen-teal brand over warm-stone
neutrals with a restrained gold accent, built on Tailwind v4 with Radix primitives for behaviour and
accessibility. Typography pairs IBM Plex Sans Arabic with Inter, both self-hosted so there is no
layout shift as fonts load.

Colours are defined in two layers: primitives (the brand ramp from `#E7F2EE` to `#0A4436`, neutrals,
accent and semantic colours) and semantic tokens that name a role (surface, sunken, text primary, text
muted, border, on-brand) rather than a colour. Components consume the semantic layer only, which is
what makes a complete dark palette possible.

That dark palette exists and is contrast-checked pair by pair. It is worth being straightforward
about its status: nothing in the application currently switches it on. It is defined, verified, and
photographed, waiting for the day someone wires a toggle to it.

---

## 14. Integration with ERPNext

ERPNext is the long-term system of record for approved supplier master data and purchase orders. The
portal does not try to replace it and does not fork it.

The boundary has three parts. A translation layer is the only code that knows ERPNext's model exists.
A transactional outbox holds events written in the same database transaction as the state change that
caused them. Adapters carry those events to the ERP with durable retries, backoff, and dead-lettering.

Two flows matter. When a supplier is approved, an event queues that creates or updates the supplier
master in the ERP, and the returned key is stored as that supplier's external identifier. When an
award is issued, an event queues that becomes an ERP purchase order, and the returned reference is
stored on the award.

Delivery is at least once and consumers are idempotent, so a retry cannot produce a duplicate side
effect. Sync status, last sync time, and errors are tracked per entity and surfaced to administrators.
Conflicts are queued for a human rather than silently overwritten.

The portal runs completely with the ERP unavailable, and pending syncs drain when it returns. No
inbound write path from the ERP has been built, deliberately: nothing has yet decided what the ERP
would be allowed to change.

---

## 15. Running the system

### Environments

Development runs everything locally, with PostgreSQL and MinIO in Docker Compose and a stubbed ERP
gateway. Staging is production-shaped but smaller, with anonymised data and an ERP sandbox. Production
runs at least two API replicas and one or two workers against managed PostgreSQL and S3-compatible
storage.

All three run the same images. Only injected configuration and scale differ.

### Deployment

Three images are built: API, worker, and web. They are immutable and promoted unchanged from staging
to production. Configuration and secrets are injected at runtime rather than baked in.

The pipeline builds, tests, scans, packages, and deploys. Staging deploys automatically from the main
branch; production needs a human approval. Database migrations use an expand and contract pattern so
schema changes stay backward-compatible and rolling deploys do not need downtime. A failed post-deploy
check rolls back to the previous image.

The gates on the pipeline today are the backend suite, the frontend suite, the container image build,
and Sonar analysis. The first three block a merge.

### Observability

Logs are structured JSON, enriched with a correlation identifier, trace identifier, user, and scope.
OpenTelemetry carries traces and metrics, and the correlation identifier ties an audit record to a
distributed trace, so an investigation can run from a browser click through the API and into the
background job that followed.

Metrics are exposed in Prometheus format, including outbox backlog. Alert rules and a Grafana
dashboard live in `ops/`. Health endpoints cover liveness and readiness, and readiness includes
PostgreSQL, migrations, object storage, and Hangfire storage.

Background job health is exposed through metrics and a read-only health endpoint rather than an
interactive dashboard, because the dashboard can retry and delete jobs and that is a larger surface
than "is this healthy" requires. The interactive dashboard stays development-only.

Logs carry no secrets and no unredacted personal data. Email jobs mint their tokens at send time
rather than accepting them as arguments, so no live token is ever sitting in a job record.

### Backup and recovery

PostgreSQL has automated backups with point-in-time recovery. Object storage is backed up separately.
Target recovery point is 15 minutes and target recovery time is four hours, both pending business
confirmation. Backups are encrypted, and the outbox survives failover so pending syncs are not lost.

---

## 16. How correctness is checked

Worth stating plainly, because it is a property of the system rather than a description of how it was
built.

The backend has 446 unit tests, 26 architecture tests, and 834 integration tests. The integration
suite runs against a real PostgreSQL instance in a container rather than an in-memory substitute, and
against the real application through its own HTTP pipeline. The architecture tests fail the build if a
dependency points the wrong way.

The frontend has 911 unit and component tests across 119 files, plus 582 browser tests: accessibility
scans of all 70 routes in both languages, keyboard operability, error association, reflow at 320
pixels, motion behaviour, and chart geometry measured in a real browser because a simulated DOM
reports every piece of text as having zero size.

Several instruments assert their own denominator. The accessibility suite parses the route list out of
the router and fails if the count changes, so a new screen cannot quietly go unscanned. The permission
catalogue is generated from the code and fails when it drifts. The OpenAPI baseline is compared
against the generated document. Each of these exists because a check that silently covers nothing
looks exactly like a check that passes.

---

## 17. Boundaries and open decisions

### What the portal deliberately does not do

It does not replace ERPNext: no general ledger, no invoicing, no payments, no inventory, no receiving.
It does not own approved supplier master data or the purchase order lifecycle. It does not run reverse
auctions or live bidding. It does not handle contract signature, logistics, or supplier payments. It
does not offer public unauthenticated browsing. And it does not invent Syrian legal, registration, or
tax rules: those fields are captured generically and flagged for confirmation.

### Decisions needed from the Ministry

These are questions engineering is not in a position to answer. None of them is stopping work, because
in each case the reversible option was taken and the reasoning recorded where the next person will
find it.

**Which document types are award-critical.** Expiry of an award-critical document automatically
suspends a supplier. The mechanism is built and tested, but every seeded type has the flag off, so it
currently suspends nobody. Flagging a type the Ministry would not have chosen blocks real suppliers,
and reinstating them later does not undo having been blocked. The answer is a data change, not a
deployment.

**Whether the required document set depends on category.** Today every supplier is asked for the same
set. The rule says it should depend on what they supply. Building the join needs a table of category
to required document types, and guessing at one risks building the wrong shape entirely.

**Whether a missing required document blocks approval.** Two of our own artifacts disagree: the rule
text says yes, the code says no and cites a product decision. One of them is wrong, and it needs
settling rather than being decided by whoever is more confident.

**Whether the Ministry sees commercial figures.** Currently gated to aggregate metrics. A decision
exists to allow figures for the demonstration, but real data stays gated until this is settled.

**Approval hierarchy and evaluation shape.** Whether award approval is single-step or a chain, and
whether a two-envelope process is mandatory, both shape data models that are painful to migrate after
real scoring has begun.

### Deferred work

An ASVS Level 2 review, a full manual WCAG audit, and load testing are all deferred to a later pass,
by decision rather than by oversight. Performance targets (p95 under 300ms for reads, 800ms for
writes, LCP under 2.5 seconds) are specified and not yet verified under load.

---

## 18. Glossary

| Term | Meaning |
|---|---|
| Supplier | An external company or individual that registers, onboards, and bids. The portal owns the record until the ERP approves it |
| Onboarding | The reviewed path from registration to approval, governed by profile and document completeness |
| Buying entity | A hotel, a Ministry-affiliated body, or the Ministry itself. Maps to an ERP company |
| RFQ | A buyer-authored request for quotation: items, requirements, invitations, and a timeline. Internal until published |
| Invitation | The link between an RFQ and one invited supplier. It gates who may propose |
| Clarification | A structured question and answer exchange on an RFQ during the clarification window |
| Proposal | A supplier's single response to one RFQ. Priced items, commercial terms, technical response |
| Offering | A supplier's catalogue entry, with category-shaped attributes |
| Evaluation template | A reusable weighted-criteria definition whose weights sum to 100 |
| Criterion | One weighted scoring dimension inside a template |
| Consolidation | Merging independent evaluator scores into ranked, weighted results |
| Recommendation | The evaluation-backed proposal of a winner, entering the approval chain |
| Award | The approved decision granting the RFQ to a winning proposal |
| Reference code | The opaque public identifier, such as `RFQ-2026-000123` |
| External identifier | The nullable ERPNext naming-series string mapped to a synced entity. Never an integer key |
| Outbox | The transactional event store bridging a domain change to handlers and to the ERP |
| Translation layer | The boundary protecting the portal's domain from ERPNext's model |
| Row scoping | The rule limiting suppliers to their own data and buyers to their organisation's |
| Two-envelope | Separating commercial from technical content so one can be assessed without the other |

---

## Where the detail lives

This document is the whole picture. The specification set in `docs/` holds the detail behind it:
product vision and business requirements, functional and non-functional requirements, personas and
user journeys, business rules and processes, the architecture and domain and database models, the API
and security and observability architectures, the deployment architecture, integration contracts, and
the user experience set covering information architecture, screens, flows, the design system,
accessibility, right-to-left behaviour, and writing style.

`DECISIONS-TAKEN.md` records every ruling made in the course of building that no document settled,
with the reasoning. `PERMISSIONS.md` is generated from the code and is always current.
