# Glossary — MOTS Supplier Portal

> **Status:** Baseline v1 · **Date:** 2026-08-26 · **Owner:** Product / Principal Architect
> Canonical domain vocabulary for the MOTS Supplier Portal. Aligned with the
> [Foundational Decisions](../architecture/00-foundational-decisions.md), the
> [Discovery Report](./DISCOVERY-REPORT.md), and the ERPNext integration surface. Arabic terms are
> provided where they aid the Arabic-first UI; Arabic wording is `[ASSUMPTION]` where not yet
> business-confirmed and should be treated as a working translation.

## Conventions

- **Portal-owned** vs **ERP-owned** notes which system is authoritative (see canonical §1).
- ERPNext mappings reference the `buying` doctypes catalogued in the Discovery Report §3.1.

---

## A

**ACL — Anti-Corruption Layer** · *(portal integration)*
The isolation boundary that translates between the portal's clean domain model and ERPNext's schema
and semantics. All ERP reads/writes pass through adapters here so that ERPNext concepts never leak
into the portal's `Domain`/`Application` layers. Pairs with the Outbox for async, resilient sync.

**Address** · العنوان
A postal/physical address value object attached to a Supplier, Branch, or Organization. A Supplier may
hold multiple addresses; maps to ERPNext linked `Address` doctype on integration.

**Aggregate / Aggregate Root** · *(domain)*
A cluster of domain objects treated as one transactional consistency boundary, referenced by its root
(e.g. `Supplier`, `RFQ`, `Proposal`, `Evaluation`, `Award`). Invariants are enforced at the root;
cross-aggregate references use IDs, not object graphs.

**Approval** · الموافقة / الاعتماد
A recorded decision by an authorized approver that advances a state machine (e.g. RFQ
`InternalReview → Approved`, or award `PendingApproval → Approved`). Default is a single configurable
approver per step ([ASM-040](./ASSUMPTIONS.md), [ASM-041](./ASSUMPTIONS.md)); multi-level chains are an
open question ([OQ-004](./OPEN-QUESTIONS.md)).

**Approval Hierarchy** · تسلسل الموافقات
The (configurable) chain of approvers required to publish an RFQ or finalize an award. See
[OQ-004](./OPEN-QUESTIONS.md).

**Audit Log** · سجل التدقيق
Append-only record of every state transition and permission-guarded action: actor, timestamp,
from→to state, reason, and correlation ID. Retained indefinitely in v1 ([ASM-085](./ASSUMPTIONS.md));
read-gated by `audit.read` and surfaced in Ministry governance views.

**Award** · الترسية / الإرساء
Portal aggregate representing the outcome of an RFQ: a `Recommendation`, one or more `Approval`s, the
final `AwardDecision`, an optional `ExternalPurchaseOrderRef`, and `AwardState`. On approval the portal
emits an event that the ACL maps to an ERPNext **Purchase Order** ([ASM-071](./ASSUMPTIONS.md)).

**AwardState** · حالة الترسية
The award state machine: `Recommended → PendingApproval → Approved | Rejected → Awarded → (Outbox → ERP PO)`.

## B

**BankAccount** · الحساب البنكي
Supplier banking details (bank, branch, account/IBAN) captured during onboarding, stored encrypted at
rest; not externally verified in v1 ([ASM-025](./ASSUMPTIONS.md)). Maps to ERPNext supplier `accounts`.

**Branch** · الفرع
A supplier's operating location/branch under the single `Supplier` legal entity aggregate.

**Buying Entity** · جهة الشراء / الجهة المشترية
An organization that publishes RFQs and awards contracts — a hotel or a MOT-affiliated body. Modeled
as an **Organization** of the buyer type. Distinct from the Ministry (which observes) and from
Suppliers (who bid). A Supplier may transact with multiple buying entities (many-to-many).

## C

**Category** · التصنيف / الفئة
Reference-data tree classifying suppliers and RFQ items (e.g. catering, furnishings, maintenance).
Maps to ERPNext **`supplier_group`** on integration ([ASM-073](./ASSUMPTIONS.md)).

**Clarification** · الاستيضاح / طلب التوضيح
A question-and-answer thread on a published RFQ between an invited supplier and the procurement team.
Private to the asking supplier by default, with an option to broadcast the answer to all invited
suppliers ([ASM-044](./ASSUMPTIONS.md)); broadcast-all fairness rules are an open question
([OQ-008](./OPEN-QUESTIONS.md)).

**CommercialTerms** · الشروط التجارية
Value object on a Proposal capturing pricing terms, validity, payment terms, delivery/Incoterm, and
currency.

**Consolidation / ConsolidatedResult** · التجميع / النتيجة المجمعة
The step where independent evaluator scores are combined (weighted average across weighted criteria)
into a single ranked result per proposal ([ASM-051](./ASSUMPTIONS.md)). Occurs after evaluators submit
independently and blind ([ASM-050](./ASSUMPTIONS.md)).

**Correlation ID** · معرّف الترابط
An identifier propagated across a request, its audit entries, background jobs, and Outbox/ERP messages
so a single business action can be traced end-to-end (Serilog + OpenTelemetry).

**Currency** · العملة
Reference data for monetary units; default **SYP (Syrian Pound)**, configurable. Proposals may use
different currencies; there is no FX conversion engine in v1 ([ASM-030](./ASSUMPTIONS.md),
[OQ-007](./OPEN-QUESTIONS.md)).

## D

**Document** · المستند / الوثيقة
Shared abstraction for uploaded files across the domain (supplier documents, RFQ attachments, proposal
documents), stored via the `IFileStorage` abstraction (local disk dev / S3-compatible prod).

**DocumentType** · نوع المستند
Reference data defining a class of onboarding document (e.g. commercial registration, tax card, bank
letter), with flags for mandatory-ness and expiry behavior. The authoritative list is an open question
([ASM-022](./ASSUMPTIONS.md), [OQ-013](./OPEN-QUESTIONS.md)).

## E

**Evaluation** · التقييم
Aggregate coordinating the scoring of proposals for an RFQ: `EvaluationAssignment[]`,
`EvaluatorScore[]`, `ConsolidatedResult`, and `EvaluationState`.

**EvaluationAssignment** · إسناد التقييم
The link assigning a specific evaluator to score specific proposals/criteria for an RFQ.

**EvaluationState** · حالة التقييم
State machine: `NotStarted → Assigned → InProgress → EvaluatorSubmitted → Consolidated → Finalized`.

**EvaluationTemplate** · قالب التقييم
A configurable, reusable set of weighted `Criterion`s applied to an RFQ. Validated against ERPNext's
**Supplier Scorecard** model (Discovery §3.1), confirming a configurable weighted-criteria approach
rather than hard-coded criteria.

**Evaluation Criterion** · معيار التقييم
A single dimension scored during evaluation, defined by: `name`, `weight`, `max` score, an optional
`threshold`, and a `scoring type`. Analogous to ERPNext scorecard `criteria_name/max_score/weight`.

**Evaluator** · عضو لجنة التقييم / المقيّم
Persona (`evaluator`) who scores proposals against the template. Evaluators score **independently and
blind** to peers before consolidation ([ASM-050](./ASSUMPTIONS.md), [OQ-005](./OPEN-QUESTIONS.md)) —
"evaluator blindness".

**EvaluatorScore** · درجة المقيّم
One evaluator's score for a proposal on a criterion; hidden from peers until the `Consolidated` state.

**ExternalId** · المعرّف الخارجي
Nullable **string** on every ERP-syncable aggregate holding the ERPNext naming-series key
(e.g. `SUP-2026-00001`). The portal never stores an integer FK to ERP; entities exist without an
`ExternalId` and are back-filled on sync ([ASM-072](./ASSUMPTIONS.md)).

## G

**GUIDv7** · *(technical)*
Time-ordered UUID used as internal primary keys. Never exposed in URLs — public references use opaque
codes like `RFQ-2026-000123` instead.

## H

**Hijri Date** · التاريخ الهجري
The Islamic lunar calendar. Gregorian is the v1 default; Hijri display is a future/optional feature and
its role in official documents/deadlines is an open question ([ASM-002](./ASSUMPTIONS.md),
[OQ-017](./OPEN-QUESTIONS.md)).

## I

**Incoterm** · شروط التسليم الدولية (إنكوترمز)
Standard international commercial delivery term (e.g. EXW, FOB, CIF, DDP) indicating cost/risk transfer
between buyer and supplier. Reference data on RFQ and Proposal; maps to ERPNext `incoterm`.

**Invitation** · الدعوة
An invited-supplier record on an RFQ (`Invitation[]`), tracking who was invited, notification status,
and quote status. Maps to ERPNext **`Request for Quotation Supplier`** (`supplier, contact,
quote_status, email_sent`).

## L

**LegalInfo** · المعلومات القانونية
Value object holding a supplier's legal/registration identity (legal name, legal form, commercial
registration number, tax identifier, registration authority/date). Captured **generically** — Syrian
formats and validation rules are not invented ([ASM-020](./ASSUMPTIONS.md),
[OQ-012](./OPEN-QUESTIONS.md)).

## M

**MFA — Multi-Factor Authentication** · المصادقة متعددة العوامل
Second-factor sign-in. Available (ASP.NET Core Identity 2FA) but not globally mandatory in v1;
enforcement per high-privilege role is an open question ([ASM-081](./ASSUMPTIONS.md),
[OQ-018](./OPEN-QUESTIONS.md)).

**Ministry (of Tourism / MOT)** · وزارة السياحة
The governance observer of the ecosystem. Modeled as an Organization of the ministry type; the
`ministry_viewer` persona has **read-only, cross-organization** access to aggregate governance metrics.
What commercial/identity data the Ministry may see is a key open question ([ASM-060](./ASSUMPTIONS.md),
[OQ-001](./OPEN-QUESTIONS.md)).

## N

**Naming Series** · سلسلة التسمية
ERPNext's string primary-key scheme (e.g. `SUP-.YYYY.-00001`). The reason portal `ExternalId`s are
strings, not integers. Portal public references (`RFQ-2026-000123`) are independent and need not mirror
ERP naming series ([ASM-086](./ASSUMPTIONS.md)).

**Notification** · الإشعار
Aggregate + delivery abstraction for in-app and email messages (localized ar/en). SMS/push are future
channels ([ASM-084](./ASSUMPTIONS.md)).

**NFR — Non-Functional Requirement** · المتطلبات غير الوظيفية
Quality targets: availability 99.5%, API p95 < 300ms reads / < 800ms writes, LCP < 2.5s, INP < 200ms,
WCAG 2.2 AA, OWASP ASVS L2 (canonical §9).

## O

**Offering** · العرض / المنتج أو الخدمة المقدمة
A product or service a supplier declares it can provide (stored in the `Supplier` aggregate, flexible
via JSONB). Informational in v1 — it does not by itself gate bidding eligibility
([ASM-045](./ASSUMPTIONS.md), [OQ-021](./OPEN-QUESTIONS.md)).

**Onboarding** · التأهيل / التسجيل والاعتماد
The end-to-end process by which a registered supplier becomes an approved, active vendor: profile
completion, document upload, and compliance review.

**OnboardingState** · حالة التأهيل
State machine: `Draft → EmailVerified → ProfileInProgress → Submitted → UnderReview →
(InfoRequested → Resubmitted → UnderReview)* → Approved | Rejected`; post-approval lifecycle
`Active ↔ Suspended → Deactivated`.

**Organization** · المنظمة / الجهة
Buying-entity/ministry aggregate (Hotel, MOT body, or Ministry), containing `OrgUnit`s. Procurement and
evaluator personas are scoped to their `OrganizationId`.

**Outbox / OutboxMessage** · صندوق الصادر
Transactional outbox pattern: domain/integration events are written in the same DB transaction as the
state change, then dispatched asynchronously (Hangfire) with retries and dead-lettering. Guarantees the
portal never blocks core flows on ERP availability.

## P

**Permission** · الصلاحية
An atomic `resource.action` grant (e.g. `rfq.publish`, `proposal.submit`, `evaluation.score`,
`award.approve`, `audit.read`). Composed into Roles.

**Persona** · الشخصية / الدور الوظيفي
A canonical user archetype (e.g. `supplier_admin`, `procurement_officer`, `evaluator`,
`ministry_viewer`, `system_admin`) — see canonical §3 and `docs/product/PERSONAS.md`.

**Procurement Officer / Manager** · موظف / مدير المشتريات
Buying-entity personas who author RFQs (`procurement_officer`) and approve publication/award
(`procurement_manager`).

**Proposal** · العرض / عرض السعر (المقدَّم من المورّد)
A supplier's response to an RFQ: `ProposalItem[]` (line pricing), `ProposalDocument[]`,
`CommercialTerms`, `TechnicalResponse`, validity, and `ProposalState`. Exactly **one per supplier per
RFQ** ([ASM-042](./ASSUMPTIONS.md)). Maps to ERPNext **`Supplier Quotation`**. Also called a
**Quotation**.

**ProposalState** · حالة العرض
State machine: `Draft → Submitted → UnderReview → (ClarificationRequested → Revised → UnderReview)* →
Shortlisted | NotSelected → AwardOffered → Awarded | Declined`; supplier-initiated `Withdrawn` allowed
while submission is open.

**Purchase Order (PO)** · أمر الشراء
The ERP-owned document created (post-integration) when an award is finalized; the portal emits an award
event the ACL maps to an ERPNext PO. Referenced in the portal only via `ExternalPurchaseOrderRef`.

## Q

**Quotation** · عرض السعر
Synonym for **Proposal** in this domain (aligns with ERPNext `Supplier Quotation`). "Proposal" is the
preferred portal term; "Quotation" appears in ERP-integration contexts.

## R

**RBAC — Role-Based Access Control** · التحكم في الوصول المبني على الأدوار
Authorization model: `resource.action` permissions grouped into admin-editable roles, enforced at the
API (policy handlers) and re-checked in the UI for affordance-hiding only.

**Recommendation** · التوصية
The proposed winning proposal produced from consolidated evaluation results; the top-ranked proposal by
default, overridable with a recorded justification ([ASM-053](./ASSUMPTIONS.md)) before award approval.

**Representative** · الممثل / مندوب المورّد
A supplier user acting on the supplier's behalf. The first verified registrant becomes the
`supplier_admin`; delegated users are `supplier_user` ([ASM-014](./ASSUMPTIONS.md)).

**Requirement** · المتطلب
A qualitative/technical condition attached to an RFQ that proposals must address (distinct from priced
`RfqItem`s).

**RFQ — Request for Quotation** · طلب عرض أسعار
Portal aggregate for a buyer's solicitation: `RfqItem[]`, `Requirement[]`, `Attachment[]`,
`Invitation[]`, `Clarification[]`, an `EvaluationTemplateRef`, a `Timeline`, and `RfqState`. The RFQ is
buyer-internal; suppliers respond via Proposals. Maps to ERPNext **`Request for Quotation`**.

**RfqItem** · بند طلب عرض الأسعار
A line item on an RFQ (description, quantity, unit of measure) that suppliers price in their proposals.

**RfqState** · حالة طلب عرض الأسعار
State machine: `Draft → InternalReview → Approved → Published → SubmissionOpen → SubmissionClosed →
UnderEvaluation → Clarification* → Shortlisting → Recommendation → AwardApproval → Awarded → Completed`;
`Cancelled` reachable from any pre-Awarded state with reason + audit.

**Role** · الدور
A named, admin-editable set of Permissions seeded per persona.

**RowVersion** · إصدار السجل
Optimistic-concurrency token on aggregates; also guards against lost updates during ERP sync.

## S

**Shortlisting** · القائمة المختصرة
Narrowing evaluated proposals to a shortlist before recommendation (RFQ `Shortlisting` state; proposal
`Shortlisted`).

**SLA — Service-Level Agreement/Target** · اتفاقية/مستوى الخدمة
Time-bound targets on workflow steps (e.g. document review turnaround, expiry warning lead time —
default 30 days, [ASM-023](./ASSUMPTIONS.md)).

**Supplier** · المورّد
Central portal aggregate for a vendor legal entity: `ExternalId?`, `SupplierProfile`, `LegalInfo`,
`Address[]`, `Contact[]`, `Representative[]`, `Branch[]`, `BankAccount[]`, `CategoryLink[]`,
`Offering[]`, `SupplierDocument[]`, and `OnboardingState`. Portal-owned pre-approval; approved supplier
master becomes ERP-owned post-integration. Maps to ERPNext **`Supplier`**.

**Supplier Admin / Supplier User** · مدير المورّد / مستخدم المورّد
Supplier-side personas: `supplier_admin` (primary representative, can delegate) and `supplier_user`
(delegated representative).

**Supplier Group** · مجموعة الموردين
ERPNext classification of suppliers; the portal's **Category** maps to it on integration
([ASM-073](./ASSUMPTIONS.md)).

**SupplierDocument** · مستند المورّد
A document instance uploaded by a supplier against a required `DocumentType`, with its own review and
expiry lifecycle.

**Supplier Scorecard** · بطاقة أداء المورّد
The ERPNext doctype (weighted criteria + periods + standings) that validated the portal's configurable
weighted **EvaluationTemplate** approach (Discovery §3.1).

**SyncStatus** · حالة المزامنة
Field on ERP-syncable aggregates indicating integration state (e.g. not-synced / pending / synced /
failed), paired with `LastSyncedAt`.

## T

**TechnicalResponse** · العرض الفني / الرد الفني
The technical portion of a proposal answering RFQ requirements, scored under technical criteria. In v1
technical and commercial are evaluated within one template (no two-envelope separation —
[ASM-052](./ASSUMPTIONS.md), [OQ-009](./OPEN-QUESTIONS.md)).

**Tenancy** · نموذج الاستئجار / تعدد الجهات
How buying entities share the platform. v1 is a single shared instance with row-level scoping, not
physical isolation ([ASM-011](./ASSUMPTIONS.md), [OQ-003](./OPEN-QUESTIONS.md)).

**Threshold** · الحد الأدنى / العتبة
A minimum acceptable score on an evaluation criterion; a proposal scoring below threshold on a gated
criterion is flagged (and may be disqualified) during consolidation.

**Timeline (RFQ)** · الجدول الزمني
The set of key RFQ dates (publish, submission open/close, evaluation, award), governed in Syria time
([ASM-004](./ASSUMPTIONS.md)).

## U

**UnitOfMeasure (UoM)** · وحدة القياس
Reference data for quantities on RFQ/proposal line items (e.g. each, kg, night, service).

## V

**Validity** · مدة صلاحية العرض
The period a supplier's proposal/quotation remains valid for acceptance.

**Value Object (VO)** · كائن القيمة
An immutable domain object defined by its attributes, not an identity (e.g. `LegalInfo`,
`CommercialTerms`, `Address`).

## W

**Weight** · الوزن / الترجيح
The relative importance of an evaluation criterion within a template; consolidated scores are computed
as a weighted, normalized combination of criteria ([ASM-051](./ASSUMPTIONS.md)).

**Withdrawn** · مسحوب
Supplier-initiated proposal state, permitted only while the RFQ submission window is open.

---

## Change log

| Date | Change | By |
|---|---|---|
| 2026-08-26 | Initial glossary covering domain aggregates, states, RBAC, and ERP-integration terms. | Principal Architect |
