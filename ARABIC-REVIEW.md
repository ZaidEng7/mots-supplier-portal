# Arabic awaiting review

Every drafted Arabic string in the product that has not been through a native review, collected in
one place so a reviewer can read it end to end without opening any source file.

**Status: approved as drafted.** The product owner will review these against a demo; every entry
below stays marked as drafted until then. Two consistency fixes were applied afterwards and are
recorded in `DECISIONS-TAKEN.md` — **D-17** (one word for clarification) and **D-18** (screen and
export strings identical). The strings below are post-fix, so this file matches what ships.

This file is a copy. The authoritative text still lives in
`src/backend/Application/Notifications/NotificationCatalogue.jsonc`,
`src/frontend/src/i18n/config.ts` and `src/backend/Application/Reports/ReportViews.cs`. When a
string is corrected, it must be corrected there — this file is not a source.

## Why these need a review at all

`UX-WRITING.md` §1.3 requires **native authoring in professional MSA, not translation**. §10 adds
the shape a notification takes: what happened and which object first, then why it matters, then the
link. Where a document already carried an approved string, it was transcribed. Where none existed,
it was drafted in the surrounding register and marked as drafted rather than presented as settled.
Those drafts are what this file collects.

Two constraints shaped the drafting and are worth checking against, because a reviewer may
reasonably want to relax either:

- **BRULE-091** keeps personal and sensitive data out of notifications. That is enforced on the
  payload by `NotificationPayload.AllowedKeys`, but it was applied to the words too — which is why
  no body carries a price, a rejection reason, or anyone's name. Several bodies read thinner than
  they naturally would for that reason.
- **Gender agreement** with the subject, which is not always the obvious one. `الدعوة` (invitation)
  is feminine; `المورد` (supplier) is masculine; `العرض` (proposal) is masculine. Where a label
  attaches to a different noun than the reader expects, it is flagged.

Reference codes (`{rfqCode}`, `{proposalCode}`) are interpolated and bidi-isolated by the renderer
per RTL §5.3 — never concatenated into the sentence by hand — so they can be read as opaque tokens.

## A correction to the ask

The request named **18** notification texts. The catalogue holds **23** in the main set, plus the two
from #109 counted separately below. The likely origin of the number: EPIC-15 shipped **19** (RFQ
lifecycle 5, evaluation 6, award 7, proposal withdrawn 1), and T3-36 later added **4** more for the
three RFQ states that had been unreachable. All 23 are listed. Silently dropping five to match a
count is the one thing that would make this file useless.

One of the 23 is **not** fully drafted: `award.erp_failed`'s Arabic **body** is transcribed from §9's
approved `sync.pending` string. Its title is drafted. It is marked accordingly rather than left in
the drafted pile, because re-reviewing approved copy wastes the reviewer's attention.

---

## Set 1 — Notification texts (23)

**Source file:** `src/backend/Application/Notifications/NotificationCatalogue.jsonc`
**Where they render:** the in-app notification centre at `/notifications` (SCR-900) and the bell in
the top bar of every shell. `NotificationMaterialiser` writes both language variants onto the
notification row at send time, so the reader sees whichever their locale selects. Types marked
*Email + in-app* also go out as email with the same words.

Each entry has a title and a body. Both are listed.

### RFQ lifecycle (5)

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `rfq.submitted_for_review` (title) | An RFQ is waiting for your review | طلب بانتظار مراجعتك | Drafted |
| `rfq.submitted_for_review` (body) | RFQ {rfqCode} was submitted for internal review. Review it to approve or return it for edits. | قُدّم الطلب {rfqCode} للمراجعة الداخلية. راجعه لاعتماده أو إعادته للتعديل. | Drafted |
| `rfq.returned_for_edits` (title) | An RFQ was returned for edits | أُعيد الطلب للتعديل | Drafted |
| `rfq.returned_for_edits` (body) | RFQ {rfqCode} was returned to you for edits. Review the comments and submit it again. | أُعيد الطلب {rfqCode} إليك للتعديل. راجع الملاحظات ثم أعد تقديمه. | Drafted |
| `rfq.approved` (title) | Your RFQ was approved | تم اعتماد الطلب | Drafted |
| `rfq.approved` (body) | RFQ {rfqCode} was approved. You can now publish it to the invited suppliers. | اعتُمد الطلب {rfqCode}. يمكنك الآن نشره للموردين المدعوين. | Drafted |
| `rfq.submission_opened` (title) | Submissions are open | فُتح باب التقديم | Drafted |
| `rfq.submission_opened` (body) | Submissions are now open for RFQ {rfqCode}. Send your proposal before the deadline. | فُتح باب التقديم للطلب {rfqCode}. قدّم عرضك قبل الموعد النهائي. | Drafted |
| `rfq.submission_closed` (title) | Submissions are closed | أُغلق باب التقديم | Drafted |
| `rfq.submission_closed` (body) | Submissions for RFQ {rfqCode} are closed. New proposals are no longer accepted. | أُغلق باب التقديم للطلب {rfqCode}. لم تعد العروض الجديدة مقبولة. | Drafted |

### Evaluation (6)

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `evaluation.opened` (title) | Evaluation has started | بدأ تقييم الطلب | Drafted |
| `evaluation.opened` (body) | Evaluation for RFQ {rfqCode} is open. Start scoring the proposals assigned to you. | فُتح تقييم الطلب {rfqCode}. ابدأ بتقييم العروض المسندة إليك. | Drafted |
| `evaluation.evaluator_submitted` (title) | All evaluators have submitted | اكتمل تقييم جميع المقيّمين | Drafted |
| `evaluation.evaluator_submitted` (body) | Every evaluator has submitted their scores for RFQ {rfqCode}. You can consolidate the results. | أرسل جميع المقيّمين درجاتهم للطلب {rfqCode}. يمكنك توحيد النتائج. | Drafted |
| `evaluation.consolidated` (title) | Evaluation results were consolidated | تم توحيد نتائج التقييم | Drafted |
| `evaluation.consolidated` (body) | The evaluation results for RFQ {rfqCode} were consolidated. Review the ranking before finalizing. | وُحّدت نتائج تقييم الطلب {rfqCode}. راجع الترتيب قبل الاعتماد. | Drafted |
| `evaluation.finalized` (title) | Evaluation was finalized | تم اعتماد نتائج التقييم | Drafted |
| `evaluation.finalized` (body) | The evaluation for RFQ {rfqCode} was finalized. | اعتُمدت نتائج تقييم الطلب {rfqCode} بشكل نهائي. | Drafted |
| `evaluation.reopened` (title) | The evaluation was reopened | أُعيد فتح التقييم | Drafted |
| `evaluation.reopened` (body) | The evaluation for RFQ {rfqCode} was reopened. Review your scores and submit them again. | أُعيد فتح تقييم الطلب {rfqCode}. راجع درجاتك وأعد إرسالها. | Drafted |
| `evaluation.evaluator_recused` (title) | An evaluator recused themselves | تنحّى أحد المقيّمين | Drafted |
| `evaluation.evaluator_recused` (body) | An evaluator stepped down from the evaluation of RFQ {rfqCode}. You may need to assign a replacement. | تنحّى أحد المقيّمين عن تقييم الطلب {rfqCode}. قد تحتاج إلى إسناد بديل. | Drafted |

### Award (7)

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `award.recommended` (title) | An award recommendation is ready | توصية ترسية بانتظار المراجعة | Drafted |
| `award.recommended` (body) | An award recommendation was recorded for RFQ {rfqCode}. Review it before routing it for approval. | سُجّلت توصية ترسية للطلب {rfqCode}. راجعها قبل توجيهها للاعتماد. | Drafted |
| `award.routed_for_approval` (title) | An award needs your approval | ترسية بانتظار اعتمادك | Drafted |
| `award.routed_for_approval` (body) | The award for RFQ {rfqCode} was routed for your approval. Review the recommendation and decide. | وُجّهت ترسية الطلب {rfqCode} لاعتمادك. راجع التوصية واتخذ قرارك. | Drafted |
| `award.approved` (title) | The award was approved | تم اعتماد الترسية | Drafted |
| `award.approved` (body) | The award for RFQ {rfqCode} was approved. You can now execute it. | اعتُمدت ترسية الطلب {rfqCode}. يمكنك الآن تنفيذها. | Drafted |
| `award.rejected` (title) | The award was rejected | رُفضت الترسية | Drafted |
| `award.rejected` (body) | The award for RFQ {rfqCode} was rejected. Review the reason and recommend again. | رُفضت ترسية الطلب {rfqCode}. راجع سبب الرفض وأعد التوصية. | Drafted |
| `award.re_recommended` (title) | The award was recommended again | أُعيدت توصية الترسية | Drafted |
| `award.re_recommended` (body) | A new award recommendation for RFQ {rfqCode} was submitted after the rejection. | أُعيد تقديم توصية ترسية الطلب {rfqCode} بعد الرفض. | Drafted |
| `award.erp_synced` (title) | The award synced to the ERP | تمت المزامنة مع نظام تخطيط الموارد | Drafted |
| `award.erp_synced` (body) | The award for RFQ {rfqCode} synced and a purchase order was created. | تمت مزامنة ترسية الطلب {rfqCode} وإنشاء أمر الشراء. | Drafted |
| `award.erp_failed` (title) | ERP sync failed | فشلت المزامنة مع نظام تخطيط الموارد | Drafted |
| `award.erp_failed` (body) | The award for RFQ {rfqCode} is recorded. Syncing to the ERP will retry automatically. | تم تسجيل ترسية الطلب {rfqCode}. ستُعاد المزامنة مع النظام تلقائياً. | **Reused** — transcribed from §9's approved `sync.pending`. No review needed on the body; the title above is drafted. |

### Proposal (1)

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `proposal.withdrawn` (title) | A proposal was withdrawn | تم سحب العرض | Drafted |
| `proposal.withdrawn` (body) | Proposal {proposalCode} on RFQ {rfqCode} was withdrawn. | سُحب العرض {proposalCode} المقدَّم على الطلب {rfqCode}. | Drafted |

### RFQ states added by T3-36 (4)

These four cover the three RFQ states `BUSINESS-PROCESSES.md` §3.1 defines but no code could reach
until T3-36, plus the recommendation event.

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `rfq.clarification_requested` (title) | A clarification is needed | مطلوب استيضاح على طلبك | Drafted |
| `rfq.clarification_requested` (body) | A clarification was requested on RFQ {rfqCode}. Review the details and respond. | طُلب استيضاح بخصوص الطلب {rfqCode}. راجع التفاصيل وقدّم ردك. | Drafted |
| `rfq.clarification_resolved` (title) | Clarification resolved, evaluation resumed | انتهى الاستيضاح واستُؤنف التقييم | Drafted |
| `rfq.clarification_resolved` (body) | The clarification on RFQ {rfqCode} is resolved and evaluation has resumed. | انتهى استيضاح الطلب {rfqCode} واستُؤنف التقييم. | Drafted |
| `rfq.shortlisting_started` (title) | Shortlisting has started | بدأ إعداد القائمة المختصرة | Drafted |
| `rfq.shortlisting_started` (body) | Shortlisting for RFQ {rfqCode} has started now that the evaluation results are consolidated. | بدأ إعداد القائمة المختصرة للطلب {rfqCode} بعد توحيد نتائج التقييم. | Drafted |
| `rfq.recommendation_recorded` (title) | An award recommendation was recorded | سُجّلت توصية الترسية | Drafted |
| `rfq.recommendation_recorded` (body) | An award recommendation was recorded for RFQ {rfqCode}. Review it before routing it for approval. | سُجّلت توصية ترسية للطلب {rfqCode}. راجعها قبل توجيهها للاعتماد. | Drafted |

---

## Set 2 — Invitation-status labels (5)

**Source file:** `src/frontend/src/i18n/config.ts`, under `status.invitation`
**Where they render:** as a `StatusChip` in four places — the supplier's RFQ list
(`SupplierRfqListPage`), the supplier's RFQ detail (`SupplierRfqDetailPage`), the supplier dashboard
(`SupplierDashboardPage`), and the buyer's RFQ detail invitations table
(`back-office/RfqDetailPage`).

`UX-WRITING.md` §7 has **no table for `InvitationStatus`**. All five members ship on the wire as
§12.4's `invitationStatus` and render as chips, so they were drafted in §7's own register rather
than left as raw enum names.

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `status.invitation.Invited` | Invited | مدعو | Drafted — masculine, because the subject is `المورد` |
| `status.invitation.Viewed` | Viewed | تمت المشاهدة | Drafted — follows §9's `تم الحفظ` construction |
| `status.invitation.Responding` | Responding | قيد الرد | Drafted — mirrors §7.3's `قيد التقييم` |
| `status.invitation.Submitted` | Submitted | مُقدَّم | **Reused** — §7.4's `Proposal:Submitted`, same concept, same word |
| `status.invitation.Declined` | Declined | معتذر عنها | Drafted — feminine, agreeing with `الدعوة`; declining an invitation is `اعتذار`, not `رفض` |

---

## Set 3 — Report-screen strings

**No document specifies this screen at all.** FEAT-19.1/19.2 name the reports; SCREEN-INVENTORY has
no `SCR-` entry for them, and §7 has no label set. Every string here is an invention written to
match the register of the screens around it. This is the set with the least documentary backing.

It has two halves that a reviewer should read together, because a heading appearing in both must
match: the **screen** and the **exported artefact**. They are separate source files today, and
several strings are duplicated between them by hand — which is how the three divergences D-18 fixed
arose, and why they will arise again. Nothing enforces that the two files agree.

### 3a — Screen

**Source file:** `src/frontend/src/i18n/config.ts`, under `reports`
**Where it renders:** `/bo/reports` (`src/routes/back-office/ReportsPage.tsx`)

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `reports.title` | Reports | التقارير | Drafted |
| `reports.from` | From | من | **Reused** — same as `procurementDashboard.from` |
| `reports.to` | To | إلى | **Reused** — same as `procurementDashboard.to` |
| `reports.state` | State | الحالة | **Reused** — §7's own column word |
| `reports.count` | Count | العدد | Drafted |
| `reports.interval` | Interval | الفترة | Drafted |
| `reports.sampleSize` | RFQs measured | عدد الطلبات المقيسة | Drafted |
| `reports.medianHours` | Median hours | الوسيط بالساعات | Drafted |
| `reports.notMeasured` | (not measured) | (غير مقيس) | Drafted — parenthesised under D-18 |
| `reports.noRows` | No data | لا توجد بيانات | Drafted |
| `reports.exportPdf` | Export PDF | تصدير PDF | Drafted |
| `reports.exportCsv` | Export CSV | تصدير CSV | Drafted |
| `reports.loadFailed` | The report could not be loaded. | تعذّر تحميل التقرير. | Drafted |
| `reports.downloadFailed` | The file could not be downloaded. | تعذّر تنزيل الملف. | Drafted |
| `reports.retry` | Try again | إعادة المحاولة | **Reused** — appears throughout |
| `reports.intervals.DraftToReview` | Draft to review | من المسودة إلى المراجعة | Drafted |
| `reports.intervals.ReviewToApproved` | Review to approved | من المراجعة إلى الاعتماد | Drafted |
| `reports.intervals.ApprovedToPublished` | Approved to published | من الاعتماد إلى النشر | Drafted |
| `reports.intervals.PublishedToSubmissionClosed` | Published to submission closed | من النشر إلى إغلاق التقديم | Drafted |
| `reports.intervals.SubmissionClosedToEvaluation` | Submission closed to evaluation | من إغلاق التقديم إلى التقييم | Drafted |
| `reports.intervals.EvaluationToAward` | Evaluation to award | من التقييم إلى الترسية | Drafted |
| `reports.procurement.title` | Procurement report | تقرير المشتريات | Drafted |
| `reports.procurement.rfqsByState` | RFQs by state | طلبات عروض الأسعار حسب الحالة | Drafted |
| `reports.procurement.cycleTime` | Cycle time | زمن الدورة | Drafted |
| `reports.procurement.awardsByState` | Awards by state | الترسيات حسب الحالة | Drafted |
| `reports.procurement.coverageFloor` | Cycle times are measured from {{date}} onward; earlier RFQs are not included. | تُقاس أزمنة الدورة من {{date}} فصاعداً؛ الطلبات الأقدم غير مشمولة. | Drafted |
| `reports.procurement.coverageNone` | No recorded transitions yet, so cycle time cannot be measured. | لا توجد انتقالات مسجَّلة بعد، لذلك لا يمكن قياس زمن الدورة. | Drafted |
| `reports.compliance.title` | Compliance report | تقرير الامتثال | Drafted |
| `reports.compliance.suppliersByState` | Suppliers by lifecycle state | الموردون حسب حالة دورة الحياة | Drafted — corrected under D-18; this section groups by `LifecycleState` |
| `reports.compliance.documentsByState` | Documents by state (latest versions) | المستندات حسب الحالة (أحدث الإصدارات) | Drafted |
| `reports.compliance.registryScope` | These counts cover every registered supplier, not only your organization. | تشمل هذه الأعداد جميع الموردين المسجَّلين، وليست مقصورة على جهتك. | Drafted |

### 3b — Exported artefact (PDF and CSV)

**Source file:** `src/backend/Application/Reports/ReportViews.cs`
**Where it renders:** inside the generated PDF and CSV, selected by the `locale` argument. These are
the words a ministry reader sees on a document they may file or forward, so they carry more weight
than a screen label.

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `ProcurementReportView.Title` | Procurement report | تقرير المشتريات | Drafted — matches `reports.procurement.title` |
| `ProcurementReportView.ArtefactName` | procurement report | تقرير المشتريات | Drafted — used in the filename and the download announcement |
| `ProcurementReportView` count columns | State / Count | الحالة / العدد | Drafted — matches the screen |
| `ProcurementReportView` section 1 | RFQs by state | طلبات عروض الأسعار حسب الحالة | Drafted — matches the screen |
| `ProcurementReportView` section 2 | Cycle time | زمن الدورة | Drafted — parenthetical dropped under D-18; now identical to the screen |
| `ProcurementReportView` cycle columns | Interval / RFQs measured / Median hours | الفترة / عدد الطلبات المقيسة / الوسيط بالساعات | Drafted — matches the screen |
| `ProcurementReportView` section 3 | Awards by state | الترسيات حسب الحالة | Drafted — matches the screen |
| `ComplianceReportView.Title` | Compliance report | تقرير الامتثال | Drafted — matches the screen |
| `ComplianceReportView.ArtefactName` | compliance report | تقرير الامتثال | Drafted |
| `ComplianceReportView` count columns | State / Count | الحالة / العدد | Drafted |
| `ComplianceReportView` section 1 | Suppliers by lifecycle state | الموردون حسب حالة دورة الحياة | Drafted — unchanged; the SCREEN moved to match it under D-18 |
| `ComplianceReportView` section 2 | Documents by state (latest versions) | المستندات حسب الحالة (أحدث الإصدارات) | Drafted — matches the screen |
| `ReportText.Hours` (unmeasured) | (not measured) | (غير مقيس) | Drafted — unchanged; the SCREEN moved to match it under D-18 |

Numerals in the artefact are converted to Eastern Arabic digits under Arabic by `ReportText.Digits`,
per R-1. That is mechanical and not a copy question.

---

## Set 4 — Proposal clarification notifications (2, from #109)

**Source file:** `src/backend/Application/Notifications/NotificationCatalogue.jsonc`
**Where they render:** the same two surfaces as Set 1 — `/notifications` and the bell.
`proposal.clarification_requested` is also sent as email, so a supplier reads it outside the portal
with no surrounding UI to lean on.

`BUSINESS-PROCESSES.md` §4.1 defines both transitions and names their notifications, but §7 has **no
proposal-clarification strings at all**, so both are drafted rather than transcribed. Neither could
fire before #109, because nothing in the code assigned `ClarificationRequested`.

| Key | English | Arabic | Drafted / reused |
|---|---|---|---|
| `proposal.clarification_requested` (title) | Clarification requested on your proposal | طلب استيضاح على عرضكم | Drafted — `إيضاح` → `استيضاح` under D-17 |
| `proposal.clarification_requested` (body) | A clarification has been requested on proposal {proposalCode} for RFQ {rfqCode}. Please review and respond. | طُلب استيضاح بشأن العرض {proposalCode} المقدَّم على الطلب {rfqCode}. يُرجى مراجعة الطلب والرد عليه. | Drafted — `إيضاح` → `استيضاح` under D-17 |
| `proposal.revised` (title) | A proposal was revised | تم تعديل العرض | Drafted |
| `proposal.revised` (body) | Proposal {proposalCode} on RFQ {rfqCode} was revised in response to a clarification request. | عُدِّل العرض {proposalCode} المقدَّم على الطلب {rfqCode} رداً على طلب الاستيضاح. | Drafted — `الإيضاح` → `الاستيضاح` under D-17 |

---

## The ones I was least sure of

Ordered by how much a wrong answer costs, not by how uncertain the wording is.

### 1. ~~`استيضاح` vs `إيضاح`~~ — RESOLVED as D-17

Settled in favour of **`استيضاح`** everywhere, per §8's glossary. The two proposal notifications from
#109 have been corrected. Worth keeping the reason on the record: these are **not synonyms** —
`استيضاح` is Form X, the act of *asking* for clarification, and `إيضاح` is Form IV, the explanation
*given in reply*. The drift had the proposal notifications naming the answer where they meant the
question, which is a wrong word rather than an inconsistent one.

Still worth a reviewer's eye: the RFQ set and the proposal set now use the same noun but are not
otherwise harmonised (`مطلوب استيضاح على طلبك` vs `طلب استيضاح على عرضكم`). Both are grammatical;
whether they should read as one voice is a copy judgement, not a glossary one.

### 2. `طلب` doing double duty — RFQ and request

`طلب` renders "RFQ" throughout (`الطلب {rfqCode}`). It is also the ordinary word for a request, and
`proposal.clarification_requested`'s body ends `يُرجى مراجعة الطلب والرد عليه` — where `الطلب` means
**the clarification request**, not the RFQ, in a sentence that named the RFQ four words earlier. A
supplier reading quickly may follow the wrong referent. I could not find a phrasing that kept §10's
shape and removed the collision.

### 3. `معتذر عنها` for Declined

Drafted on the reasoning that declining an invitation is `اعتذار` rather than `رفض`, and made
feminine to agree with `الدعوة`. Two things I am unsure of: whether the register is right for a
government portal, where the softer word may read as evasive; and whether a chip reading
`معتذر عنها` next to four masculine chips looks like a bug to a native reader even though it is
grammatically correct. `مرفوضة` would be blunter and consistent.

### 4. `نظام تخطيط الموارد` for ERP

Used in both `award.erp_synced` and `award.erp_failed`. It is the correct expansion, but it is long,
and every other Arabic string in the product is shorter than its English counterpart while these two
are markedly longer — on a notification chip that may truncate. Whether the ministry's own staff say
`نظام تخطيط الموارد`, `ERP`, or something local is a question about their vocabulary, not about
translation, and I had nothing to check it against.

### 5. Set 3's whole cycle-time vocabulary

`زمن الدورة`, `الوسيط بالساعات`, `عدد الطلبات المقيسة`, `غير مقيس`. These are statistical terms in a
procurement document, and I had **no approved Arabic anywhere in the product** to anchor them to —
`UX-WRITING.md` has no numbers vocabulary. `الوسيط` is the correct statistical median and not the
colloquial "average", which is the distinction most likely to have been lost. If the reader is
expected to be non-technical, `الوسيط` may be the wrong choice even though it is the right word.

### 6. ~~Three screen/artefact mismatches~~ — RESOLVED as D-18

All three are now identical across both surfaces. Examined one at a time, none of them turned out to
be the adaptation they looked like:

- **cycle-time heading** — the export's `(الوسيط بالساعات)` restated that section's own third column
  header, which the export carries too. Redundancy, not context. Parenthetical dropped.
- **suppliers-by-state** — the screen was simply wrong. A supplier has an `OnboardingState` as well
  as a `LifecycleState`, and this section groups by `LifecycleState`. The screen moved to the
  export's precise wording, in both languages.
- **not-measured marker** — the same table cell on both surfaces. Parentheses distinguish a marker
  from a value, which matters more in a CSV a reader may open in a spreadsheet. The screen adopted
  the parentheses.

That is why the "screen terse / export self-describing" rule was **rejected** rather than adopted:
both surfaces render the same table with the same column headers, so there is no context the export
lacks. See `DECISIONS-TAKEN.md` D-18.

### 7. `تنحّى` for recusal

`evaluation.evaluator_recused` uses `تنحّى`, which carries a connotation of stepping aside from a
position. BRULE-067's recusal is narrower: withdrawing from one evaluation, often for a declared
conflict of interest. There may be a specific term in Syrian procurement practice; I could not find
one in the documents.

### 8. Bodies that read thin because BRULE-091 emptied them

`award.rejected` says review the reason without carrying it. `rfq.clarification_requested` says
review the details without naming them. Both are deliberate and both are flagged in the catalogue,
but a reviewer may judge the Arabic to be evasive rather than terse, and that judgement is worth
having before these ship. The fix would not be to reword them — it would be to revisit BRULE-091's
scope for notification bodies.
