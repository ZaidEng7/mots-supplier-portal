# Arabic, reviewed

Every Arabic string the product authored rather than transcribed, collected in one place so a
reviewer can read it end to end without opening any source file.

**Status: accepted under D-62 by Zaid Abdulkarim, 8 September 2026** — recorded here as the
accepting reviewer, which is what D-62 asked for and what was missing until now. The markers that
meant "authored here, awaiting a native reviewer" have been removed from this file and from
`src/frontend/src/i18n/config.ts` in one pass — 438 in the catalogue and 155 here. Where a marker was
a whole table cell it now reads **authored**, which is the fact it was always carrying: this string
was written for the product rather than transcribed from an approved source. `[reused]` stays exactly
where it was, because it says where a string came from and not what state it is in.

**The acceptance has two tiers, and the difference matters.** D-62 was recorded on 2026-09-08 against
the strings that existed then, and those are **reviewed and accepted**. The roughly sixty strings
added afterwards — the phase 1 to 4 sections at the end of this file, covering the five new screens
and the four Ministry screens — are **accepted for the demonstration build without a line-by-line
read**. They ship on those terms; they are not recorded as reviewed. A proper read of them is
required before any real tender runs on this system, and a reviewer doing it should start at
*Phase 3 · SCR-402 and SCR-307* and read to the end of the file. See D-65.

Two consistency fixes were applied before the acceptance and are recorded in `DECISIONS-TAKEN.md` —
**D-17** (one word for clarification) and **D-18** (screen and export strings identical). The strings
below are post-fix, so this file matches what ships.

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

---

## Set 5 — batch 10 (A-1, A-4, A-9, and the admin surfaces)

Every string below is **drafted and marked**, and ships per the product owner's standing approval. The
product owner reviews them against a demo.

### A-9 — the two new proposal states (`status.proposal.*`)

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `status.proposal.Lapsed` | Window closed | انتهت المهلة | Status chip, supplier proposal list and dashboard | authored |
| `status.proposal.Cancelled` | RFQ cancelled | ملغى مع الطلب | Same | authored |

### A-9 — the two new notifications (`NotificationCatalogue.jsonc`)

| Type | English title / body | Arabic title / body | Source |
|---|---|---|---|
| `proposal.lapsed` | "Your proposal was not submitted in time" / "The submission window for RFQ {rfqCode} closed before your draft was submitted, so it was not included in the evaluation." | «انتهت مهلة تقديم عرضك» / «أُغلقت مهلة التقديم لطلب عرض الأسعار {rfqCode} قبل تقديم مسودتك، ولم تُدرَج في التقييم.» | authored |
| `proposal.cancelled` | "The RFQ was cancelled" / "RFQ {rfqCode} was cancelled, so your proposal is closed and will not be evaluated." | «أُلغي طلب عرض الأسعار» / «أُلغي طلب عرض الأسعار {rfqCode}، وبذلك أُغلق عرضك ولن يُقيَّم.» | authored |

### A-1 — the surfaced tie (`comparison.*`)

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `comparison.tieUnresolved` | Unresolved tie | تعادل غير محلول | Badge on a tied rank cell | authored |
| `comparison.tieTitle` | A tie in the ranking needs a decision | تعادل في الترتيب يحتاج قراراً | Tie panel heading | authored |
| `comparison.tieBody` | These bids are equal on every tie-break rule. Choose the one that ranks first and say why; no award can be recommended until you do. | تساوت العروض التالية في كل معايير الترجيح. اختر العرض الأول مع بيان السبب؛ لا يمكن الترسية قبل ذلك. | Tie panel body | authored |
| `comparison.tieReason` | Reason for choosing {{code}} | سبب اختيار {{code}} | Input label | authored |
| `comparison.tieReasonPlaceholder` | Reason for the decision | سبب القرار | Input placeholder | authored |
| `comparison.tieResolve` | Confirm the order | تثبيت الترتيب | Button | authored |
| `comparison.tieResolved` | The order is confirmed | تم تثبيت الترتيب | Toast | authored |
| `comparison.tieResolveFailed` | Could not confirm the order | تعذّر تثبيت الترتيب | Toast | authored |

### A-4 — the broadcast notice (`rfq.clarifications.*`)

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `rfq.clarifications.broadcastNotice` | The answer goes to every invited supplier. The asker is not named. | يُرسل الجواب إلى جميع المدعوين دون ذكر السائل. | Under the answer field, replacing the removed "publish immediately" checkbox | authored |

### Batch 9's admin surfaces, carried forward for the same review

`adminOverview.*` (SCR-700), `systemSettings.*` (SCR-724), `notificationTemplates.*` (SCR-715),
`register.closedTitle` / `register.closedBody`, `rfq.attachments.*` and `supplierRfq.attachments.*` —
all marked `` at their definitions in `src/frontend/src/i18n/config.ts`.

### A-5 / A-6 — the review target and the deadline reason

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `review.reviewTarget` | Target date | الموعد المستهدف | Review queue column header | authored |
| `rfq.deadline.reason` | Reason for the change | سبب التغيير | Buyer's deadline control | authored |
| `supplierRfq.deadlineChanged.title` | The submission deadline changed | تغيّر موعد إغلاق التقديم | Card on the supplier's RFQ | authored |

### A-8 — the declaration and the pseudonyms

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `evaluation.my.anonymousBidder` | Bidder identity withheld during scoring | هوية المورد محجوبة أثناء التقييم | Badge beside each bid | authored |
| `evaluation.my.declaration.title` | Conflict of interest declaration | إقرار تعارض المصالح | Declaration step heading | authored |
| `evaluation.my.declaration.body` | These are the suppliers taking part… | هذه أسماء الموردين المشاركين… | Declaration step body | authored |
| `evaluation.my.declaration.noConflict` | No conflict — continue | لا يوجد تعارض — متابعة | Button | authored |
| `evaluation.my.declaration.hasConflict` | I have a conflict — recuse me | لديّ تعارض — تنحّي | Button | authored |
| `evaluation.my.declaration.reasonLabel` | Reason for recusal | سبب التنحّي | Input label | authored |
| `evaluation.my.declaration.failed` | Could not record the declaration | تعذّر تسجيل الإقرار | Toast | authored |

**The bidder pseudonyms are generated, not catalogued**: `Bidder A` / «مورّد أ», using the Arabic
**abjad** letter order (أ ب ج د هـ و ز ح ط ي …) rather than the alphabetical one, because abjad is what
an Arabic reader expects for enumeration. Worth a specific check by the reviewer — see
`BidderLabel` in `EvaluationHandlers.cs`.

### A-2 — the envelope picker and the buyer's expectation

| Key | English | Arabic | Renders | Source |
|---|---|---|---|---|
| `proposal.envelope` | Envelope | المغلف | Label on the upload picker | authored |
| `proposal.envelopeCommercial` | Commercial envelope | المغلف المالي | Picker option | [reused] §7's own term |
| `proposal.envelopeTechnical` | Technical envelope | المغلف الفني | Picker option | [reused] §7's own term |
| `proposal.envelopeExpected.Technical` | This document is expected in the technical envelope. | يُتوقع أن يكون هذا المستند في المغلف الفني. | Under a requirement | authored |
| `proposal.envelopeExpected.Commercial` | This document is expected in the commercial envelope. | يُتوقع أن يكون هذا المستند في المغلف المالي. | Under a requirement | authored |

### T-077 — the staff accounts table (SCR-701 / SCR-702)

| Key | English | Arabic | Source |
|---|---|---|---|
| `staff.accountsTitle` | Staff accounts | حسابات الموظفين | authored |
| `staff.noAccounts` | No accounts | لا توجد حسابات | [reused] |
| `staff.inactive` | Deactivated | معطّل | authored |
| `staff.mfaOn` | Two-factor enrolled | التحقق بخطوتين مُفعّل | authored |
| `staff.sessions` | Active sessions: {{count}} | جلسات نشطة: {{count}} | authored |
| `staff.deactivate` | Deactivate | تعطيل | authored |
| `staff.reactivate` | Reactivate | إعادة التفعيل | authored |
| `staff.resetMfa` | Reset two-factor | إعادة ضبط التحقق بخطوتين | authored |
| `staff.roleChanged` | The role was changed | تم تغيير الدور | authored |
| `staff.mfaReset` | Two-factor was reset | تمت إعادة ضبط التحقق بخطوتين | authored |
| `staff.errors.loadFailed` | Could not load the staff accounts | تعذّر تحميل حسابات الموظفين | authored |
| `staff.errors.updateFailed` | Could not complete that action | تعذّر تنفيذ الإجراء | authored |
| `staff.errors.cannotActOnSelf` | You cannot do that to your own account. | لا يمكنك تنفيذ هذا الإجراء على حسابك. | authored |
| `staff.errors.wouldLockOutAdministration` | The last active system administrator cannot be deactivated. | لا يمكن تعطيل آخر مسؤول نظام مفعّل. | authored |

### B-1 — the reachable audit trail, the clarification request, and the ERP notice

| Key | English | Arabic | Source |
|---|---|---|---|
| `settings.auditTitle` | My account activity | سجل نشاط حسابي | authored |
| `settings.auditHint` | The most recent events recorded against your account, newest first. | أحدث الأحداث المسجّلة على حسابك، من الأحدث إلى الأقدم. | authored |
| `settings.auditExport` | Download the trail (CSV) | تنزيل السجل (CSV) | authored |
| `comparison.clarifyTitle` | Ask a bidder to clarify | طلب استيضاح من مورد | authored |
| `comparison.clarifyBody` | Ask a supplier to explain something about their bid… | اطلب من المورد توضيحاً حول عرضه… | authored |
| `comparison.clarifyAsk` | Request clarification | طلب استيضاح | [reused] §8's «استيضاح» per the batch-9 glossary ruling |
| `adminOverview.erpNotConfigured` | No real ERP integration is configured | لا يوجد ربط فعلي بنظام ERP | authored |
| `adminOverview.erpNotConfiguredBody` | Messages are written to the log and sent nowhere… | تُسجَّل الرسائل في السجل ولا تُرسل إلى أي نظام خارجي… | authored |

### T-080 — the reference-data editor (SCR-710 / SCR-711 / SCR-712)

Most of the row labels are reused: the five table names already existed for SCR-700's tiles, and
`تعطيل` / `إعادة التفعيل` are the same pair T-077's staff table uses, so an administrator who has
deactivated an account meets the same word when deactivating a code.

The two notices carry the weight. D-28 is a rule an administrator has to be told rather than
discover, so `noDeleteNotice` says both halves of it — that deletion is impossible, and that
deactivation does not touch what existing records say — and `inactiveNotice` explains why retired
codes are still on the page. Both are long by the register's standards, and deliberately: a shorter
sentence here reads as an apology for a missing button rather than a statement of policy.

| Key | English | Arabic | Source |
|---|---|---|---|
| `referenceAdmin.title` | Reference data | إدارة البيانات المرجعية | authored |
| `referenceAdmin.subtitle` | Add, rename, and deactivate the codes RFQs and supplier profiles are built from. | إضافة وتعديل وتعطيل الرموز المرجعية التي تستخدمها الطلبات وملفات الموردين. | authored |
| `referenceAdmin.tableLabel` | Reference table | الجدول المرجعي | authored |
| `referenceAdmin.addTitle` | Add a code | إضافة رمز جديد | authored |
| `referenceAdmin.code` | Code | الرمز | [reused] §7's own term |
| `referenceAdmin.nameAr` / `nameEn` | Name (Arabic) / Name (English) | الاسم بالعربية / الاسم بالإنجليزية | [reused] matches the onboarding form |
| `referenceAdmin.deactivate` / `reactivate` | Deactivate / Reactivate | تعطيل / إعادة التفعيل | [reused] matches T-077's staff table |
| `referenceAdmin.active` / `inactive` | Active / Inactive | مفعّل / معطّل | [reused] §7's own terms |
| `referenceAdmin.required` | Required | إلزامي | [reused] §7's own term |
| `referenceAdmin.created` | Code added | تمت إضافة الرمز | authored |
| `referenceAdmin.renamed` | Name saved | تم حفظ الاسم | authored |
| `referenceAdmin.empty` | This table has no codes | لا توجد رموز في هذا الجدول | authored |
| `referenceAdmin.noDeleteNotice` | Codes cannot be deleted. A code is referenced by existing records; deactivating it keeps it out of new selections without changing what those records say. | لا يمكن حذف الرموز. الرمز مستخدم في سجلات قائمة، والتعطيل يمنع اختياره في الطلبات الجديدة دون التأثير على السجلات السابقة. | D-28 |
| `referenceAdmin.inactiveNotice` | Deactivated codes stay listed here, so deactivation does not read as deletion. | تظهر الرموز المعطّلة في هذه القائمة حتى تبقى مرئية لمن عطّلها. | D-28 |
| `referenceAdmin.errors.duplicateCode` | That code already exists on this table. | هذا الرمز موجود بالفعل في هذا الجدول. | authored |
| `referenceAdmin.errors.createFailed` | Could not add the code | تعذّرت إضافة الرمز | authored |
| `referenceAdmin.errors.updateFailed` | Could not save the change | تعذّر حفظ التغيير | authored |
| `referenceAdmin.errors.loadFailed` | Could not load reference data | تعذّر تحميل البيانات المرجعية | authored |

### A-7 — ownership, the reassignment, and the approver nomination

Two terms need a decision from a reviewer rather than a preference from me.

**`المسؤول` for "owner".** §7 has no word for this because nothing in the documents gives an RFQ an
owner. The alternatives considered were `المالك` (literally "owner", but it reads as ownership of
property — wrong for a civil servant handling a file) and `المسند إليه` ("the one assigned to it",
accurate but clumsy as a column header). `المسؤول` — "the one responsible" — says what A-7 actually
means, which is accountability rather than possession, and it is the word the notification body
already needs: «أصبحت مسؤولاً عن الطلب».

**`المعتمِد` for "approver",** with the kasra on the mīm, from §3.1's own `اعتماد` family — so the
person and the act they perform share a root, the way `المراجع`/`مراجعة` already do on the review
side. Note the vowel: `المعتمَد` (fatḥa) would mean the thing that was approved.

The handover reason is a separate string from the deadline reason (`سبب النقل` against
`سبب التغيير`) rather than a shared "reason": both appear on the same screen, and in English the two
had to be distinguished for the same reason — one accessible name cannot belong to two fields.

| Key | English | Arabic | Source |
|---|---|---|---|
| `rfq.fields.owner` | Owner | المسؤول | A-7 |
| `rfq.unassigned` | Unassigned | غير مُسند | authored |
| `rfq.ownerFilter.label` | Filter by owner | تصفية حسب المسؤول | authored |
| `rfq.ownerFilter.me` | Mine | المُسندة إليّ | authored |
| `rfq.ownerFilter.unassigned` | Unassigned | غير مُسندة | authored |
| `rfq.ownerFilter.empty.me` | No RFQs are assigned to you | لا توجد طلبات مُسندة إليك | authored |
| `rfq.ownerFilter.empty.unassigned` | Every RFQ has an owner | كل الطلبات مُسندة إلى مسؤول | authored |
| `rfq.ownership.title` | Ownership | المسؤول عن الطلب | authored |
| `rfq.ownership.help` | One officer is responsible for taking this RFQ forward. A manager can hand it to someone else at any point; the change and the reason are recorded in the audit trail. | يُسند الطلب إلى موظف واحد يكون مسؤولاً عن متابعته. يمكن للمدير نقل المسؤولية في أي وقت، ويُسجَّل النقل والسبب في سجل التغييرات. | authored |
| `rfq.ownership.ownerLabel` | Owner | المسؤول | authored |
| `rfq.ownership.approverLabel` | Approver | المعتمِد | see the note above on the vowel |
| `rfq.ownership.newOwner` | New owner | المسؤول الجديد | authored |
| `rfq.ownership.reason` | Reason for the handover | سبب النقل | authored |
| `rfq.ownership.reassign` | Reassign | نقل المسؤولية | authored |
| `rfq.ownership.reassigned` | Ownership reassigned | تم نقل المسؤولية | authored |
| `rfq.ownership.nominateApprover` | Choose an approver | تحديد المعتمِد | authored |
| `rfq.ownership.anyManager` | Any manager | أي مدير | authored |
| `NEW_OWNER_REQUIRED` | A new owner must be chosen. | يجب تحديد المسؤول الجديد. | §7.2 catalogue |
| `rfq.reassigned` (notification title) | An RFQ was assigned to you | أُسند إليك طلب | authored |
| `rfq.reassigned` (notification body) | You are now responsible for RFQ {rfqCode}. Open it to see what it is waiting for. | أصبحت مسؤولاً عن الطلب {rfqCode}. راجعه لمتابعة الخطوة التالية. | authored |

### SCR-720 — the audit explorer (T-079)

`سجل التغييرات` is reused: it is the term already used for the supplier's own trail, and the two
screens read the same rows through different scopes, so one name is correct rather than economical.

Two strings carry a decision rather than a translation. `إجراء تلقائي من النظام` for a row with no
actor — literally "an automatic action by the system" — rather than the bare `النظام`, because a
single word in the actor column reads as the name of a person called "the System". And the empty
states are two different sentences: `لا توجد سجلات` (the log is empty) against
`لا توجد سجلات تطابق عوامل التصفية` (the filter matched nothing), because on this screen the first
would tell an administrator the platform has recorded nothing at all.

`المنفِّذ` for "actor", with the shadda on the fā, from `تنفيذ` — the one who carried the action out.
`الفاعل` was rejected: it is the grammatical term for a subject and reads as a discussion of syntax.

| Key | English | Arabic | Source |
|---|---|---|---|
| `auditExplorer.title` | Audit log | سجل التغييرات | [reused] matches the supplier's own trail |
| `auditExplorer.subtitle` | Search and export the platform-wide audit trail. | البحث في سجل التغييرات على مستوى المنصة وتصديره. | authored |
| `auditExplorer.filtersTitle` | Filters | عوامل التصفية | authored |
| `auditExplorer.clear` | Clear filters | إلغاء التصفية | authored |
| `auditExplorer.export` | Export (CSV) | تصدير (CSV) | [reused] matches the supplier's export |
| `auditExplorer.empty` | No audit rows | لا توجد سجلات | authored |
| `auditExplorer.emptyFiltered` | No audit rows match these filters | لا توجد سجلات تطابق عوامل التصفية | see the note above |
| `auditExplorer.filtersApplied` | Filters applied: {{filters}} | عوامل التصفية المطبَّقة: {{filters}} | authored |
| `auditExplorer.systemActor` | System | إجراء تلقائي من النظام | see the note above |
| `auditExplorer.fields.occurredAt` | When | التاريخ والوقت | [reused] |
| `auditExplorer.fields.action` | Action | الإجراء | [reused] §7's own term |
| `auditExplorer.fields.aggregate` | Record | السجل | authored |
| `auditExplorer.fields.aggregateType` | Record type | نوع السجل | authored |
| `auditExplorer.fields.aggregateId` | Record id | معرّف السجل | authored |
| `auditExplorer.fields.actor` | Actor | المنفِّذ | see the note above on the vowel |
| `auditExplorer.fields.actorUserId` | Actor id | معرّف المنفِّذ | authored |
| `auditExplorer.fields.transition` | State change | تغيّر الحالة | authored |
| `auditExplorer.fields.from` / `.to` | From date / To date | من تاريخ / إلى تاريخ | [reused] |
| `auditExplorer.errors.loadFailed` | Could not load the audit log | تعذّر تحميل سجل التغييرات | authored |
| `auditExplorer.errors.exportFailed` | Could not export the audit log | تعذّر تصدير سجل التغييرات | authored |

## Batch 11 — the screens this batch added

Same rule as every block above: §7 has no strings for any of these, so each was drafted in §7's
register and none is approved. `[reused]` marks a term §7 or an approved block already uses.

### SCR-150 · the supplier's own proposals

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `myProposals.title` | My proposals | عروضي | authored |
| `myProposals.subtitle` | Bids you have submitted or started preparing. | العروض التي قدّمتها أو بدأت إعدادها. | authored |
| `myProposals.empty` | You have not started a proposal yet | لم تقدّم أي عرض بعد | authored |
| `myProposals.continue` | Continue | متابعة الإعداد | longer than the English on purpose — «متابعة» alone reads as "next" |
| `myProposals.awardOffered` | Award offered | عُرضت عليك الترسية | [reused] §8's «ترسية» |
| `myProposals.fields.*` | RFQ / Proposal / State / Deadline / Total / Actions | الطلب / رقم العرض / الحالة / موعد الإغلاق / الإجمالي / الإجراءات | [reused] |

### SCR-155 · clarification and response

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `proposal.clarificationTitle` | Clarification requested | طلب إيضاح | §4.1's «إيضاح» |
| `proposal.clarificationNoReason` | No question was recorded with this request. | لم يُسجَّل نص الطلب. | authored |
| `proposal.clarificationHint` | Recording your response returns the proposal for re-review. Proposal lines cannot be edited at this stage. | تسجيل ردّك ينقل العرض إلى المراجعة من جديد. لا يمكن تعديل بنود العرض في هذه المرحلة. | the second sentence matters — see BRULE-050 |
| `proposal.revise` | Record response | تسجيل الردّ | deliberately NOT «تعديل», which would promise an edit the state refuses |
| `proposal.revisedTitle` | Awaiting re-review | بانتظار إعادة المراجعة | authored |
| `proposal.revisedBody` | Your response was recorded (revision {{revision}}). A procurement officer will return the proposal to review. | سُجّل ردّك (المراجعة رقم {{revision}}). سيعيد موظّف المشتريات العرض إلى المراجعة. | authored |

### SCR-902 · the account

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `account.title` | Account | الحساب | authored |
| `account.emailFixed` | Your email address cannot be changed from this screen. | لا يمكن تغيير البريد الإلكتروني من هذه الشاشة. | authored |
| `account.numeralsFollowLanguage` | Numerals follow the interface language: Arabic renders ٠-٩, English 0-9. | تتبع الأرقام لغة الواجهة: العربية تعرض ٠-٩ والإنجليزية 0-9. | the digits are literal in both — do not "translate" them |
| `account.fields.*` | Full name / Interface language / Email address | الاسم الكامل / لغة الواجهة / البريد الإلكتروني | [reused] |
| `account.languages.*` | Arabic / English | العربية / الإنجليزية | [reused] |

### SCR-040 · session expiry

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `sessionExpired.title` | Session expired | انتهت الجلسة | authored |
| `sessionExpired.body` | Your session has expired. Sign in again to carry on where you left off. | انتهت صلاحية جلستك. سجّل الدخول من جديد للمتابعة من حيث توقّفت. | the second clause is the reassurance — the work is not lost |
| `sessionExpired.totp` | Verification code (if enabled) | رمز التحقق (إن وُجد) | [reused] §7's «رمز التحقق» |
| `sessionExpired.failed` | Could not sign in. Check the details you entered. | تعذّر تسجيل الدخول. تحقّق من البيانات المُدخلة. | deliberately says nothing about WHICH detail |

### SCR-908 / SCR-907 · about and help

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `about.title` | About | حول النظام | authored |
| `about.commit` | Build reference | رقم البناء | not «الالتزام», which is the wrong sense of "commit" entirely |
| `about.correlationHelp` | Error messages include a correlation ID… | تحتوي رسائل الخطأ على «معرّف المتابعة»… | «معرّف المتابعة» is a coinage; a reviewer may prefer leaving `correlation ID` in Latin script |
| `about.legalPending` | Terms of use and the privacy notice have not been issued yet… | لم تُعتمد بعد شروط الاستخدام وإشعار الخصوصية… | authored |
| `help.title` | Help | المساعدة | [reused] |
| `help.contactPending` | A support channel has not been configured yet… | لم تُحدَّد بعد قناة الدعم… | authored |
| `help.topics.*` | six question/answer/action triples | see `i18n/config.ts` | **the longest prose in this file.** Every answer describes real behaviour; a reviewer changing the wording must not change what it claims |

### Step 3 · one label the dashboards exposed

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `ministry.unlabelledLifecycle` | Suppliers before approval | موردون قبل الاعتماد | **Screen copy, deliberately NOT a §7 state label.** `SupplierLifecycleState.None` reached the Ministry's governance screen as the literal word "None". My first attempt authored `status.onboarding.None` — and `StatusChip.test.tsx`'s coverage guard refused it, correctly: §7.1 has no row for that member, and a state label is the document's to write, not mine. So the wording moved to this screen's own caption, which says what the group counts without claiming to name a status. If §7.1 ever gains a row for `None`, the label belongs there and this fallback becomes dead |

### Phase 1 · BRULE-016 goes live (D-59)

One string was rewritten rather than added: `referenceAdmin.categoryLinksExplained` used to explain that
category links were recorded and not applied, which stopped being true the moment they were.

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `referenceAdmin.categoryLinksExplained` | A category link narrows a document type to suppliers in those categories. A type with no links stays required of every supplier. Changes take effect immediately, including for suppliers already approved. | ارتباط التصنيف يقصر نوع المستند على الموردين ضمن تلك التصنيفات. والنوع بلا ارتباطات يبقى مطلوباً من كل مورد. يسري التغيير فوراً، بما في ذلك على الموردين المعتمدين سابقاً. | **replaces the previous text, which said the links were not yet applied.** Three sentences because three separate facts an administrator cannot infer from the chips: what a link does, what NO link does, and that the change is retroactive. «يقصر … على» for "narrows to" rather than «يحدّد», which reads as "specifies" and loses the restriction |

### Phase 3 · SCR-402 and SCR-307, the two supplier directories

Two screens over one registry, which is why the two titles had to be different words rather than the same
word twice. A «دليل» is something you look a supplier up in; a «سجل» is the register itself.

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `supplierDirectory.title` | Supplier Directory | دليل الموردين | the buyer's browse |
| `supplierDirectory.subtitle` | Browse approved suppliers and their categories before sending invitations. | استعرض الموردين المعتمدين وفئاتهم قبل إرسال الدعوات. | authored |
| `supplierDirectory.searchPlaceholder` | Search by name or code… | ابحث بالاسم أو الرمز… | «الرمز» for the reference code, matching how it is labelled elsewhere |
| `supplierDirectory.empty` | No suppliers match | لا يوجد موردون مطابقون | authored |
| `supplierDirectory.error` | Could not load the directory | تعذّر تحميل الدليل | same «تعذّر» register as the other load failures |
| `supplierDirectory.noCategories` | No categories recorded | لا فئات مسجّلة | states an absence rather than leaving a blank cell |
| `supplierDirectory.fields.*` | Supplier / Code / Categories / Offerings / City / Status | المورد / الرمز / الفئات / العروض / المدينة / الحالة | [reused] every term already approved elsewhere in this file |
| `complianceDirectory.title` | Supplier Register | سجل الموردين | **deliberately not «دليل»** — this is the reviewer's audit view of the whole registry, not a lookup |
| `complianceDirectory.subtitle` | Every supplier and the state of their documents, including already-approved files. | كل الموردين وحالة مستنداتهم، بما في ذلك الملفات المعتمدة سابقاً. | the last clause is the point of the screen |
| `complianceDirectory.filterState` | Application status | حالة الطلب | «الطلب» = the onboarding application, distinct from «التعامل» below |
| `complianceDirectory.filterHealth` | Document health | حالة المستندات | literally "document status"; «صحة» would read as medical |
| `complianceDirectory.healthAttention` | Needs attention | تحتاج متابعة | feminine agreement — the subject is المستندات |
| `complianceDirectory.healthOk` | Healthy | سليمة | same agreement |
| `complianceDirectory.expired` / `expiring` / `rejected` | Expired / Expiring / Rejected: {{count}} | منتهٍ / ينتهي قريباً / مرفوض: {{count}} | [reused] §7.2's own three document labels, with a count appended |
| `complianceDirectory.fields.lifecycle` | Standing | حالة التعامل | the post-approval lifecycle, which §7.1 groups with onboarding but the screen shows in its own column |

### Phase 3 · SCR-501, the evaluator's brief

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `evaluationBrief.title` | Evaluation Brief | كراسة التقييم | «كراسة» is what a procurement file calls the instruction document itself; «ملخص» would read as a summary somebody wrote about the tender |
| `evaluationBrief.toScoring` | Go to scoring | الانتقال إلى التقييم | authored |
| `evaluationBrief.tender` | What is being bought | موضوع الطلب | authored |
| `evaluationBrief.howToScore` | Criteria and how they are scored | معايير التقييم وطريقة احتسابها | authored |
| `evaluationBrief.totalWeight` | Weights total: {{total}} | مجموع الأوزان: {{total}} | authored |
| `evaluationBrief.justificationRequired` | Justification required | يلزم تبرير | BRULE-061 |
| `evaluationBrief.noGuidance` | No guidance was recorded for this criterion | لم تُسجَّل تعليمات لهذا المعيار | **says NOT RECORDED, deliberately** — a tender that bound its template before the field existed has none, and "no guidance" would read as the author having decided there should be none |
| `evaluationBrief.noDescription` | No description was recorded for this tender | لا يوجد وصف مسجّل لهذا الطلب | same shape |
| `evaluationBrief.noThreshold` | No minimum | لا حد أدنى | authored |
| `evaluationBrief.error` | Could not load the brief | تعذّر تحميل الكراسة | authored |
| `evaluationBrief.fields.guidance` | How to score it | تعليمات التقييم | the column the whole screen exists for |
| `evaluationBrief.dimension.*` | Technical / Commercial | فني / تجاري | [reused] §7's own pair |

**One correction logged while adding these.** The `supplierDirectory`, `complianceDirectory` and
`evaluationBrief` English blocks were first inserted into the **Arabic** resource object by mistake. Because
a later duplicate key wins in a JavaScript object literal, the Arabic UI would have rendered English for all
three screens while every test passed — the English lookups were resolving through the fallback. Moved into
the `en` resource before commit; worth recording because the failure mode is silent in both directions.

### Phase 3 · SCR-604 and SCR-901

| Key | English | Arabic (drafted) | Note |
|---|---|---|---|
| `categoryCoverage.title` | Category Coverage | تغطية الفئات | SCR-604 |
| `categoryCoverage.subtitle` | Suppliers and tenders per category, and the categories nobody serves. | أعداد الموردين والطلبات لكل فئة، والفئات التي لا يخدمها أحد. | the second clause is what the screen is for |
| `categoryCoverage.uncovered` | {{count}} of {{total}} categories have no active supplier | {{count}} من {{total}} فئة بلا مورد فعّال | the headline figure |
| `categoryCoverage.flatNote` | The category list is currently flat and carries no hierarchy. | قائمة الفئات مسطّحة حالياً ولا تتضمّن تصنيفاً هرمياً. | **states an absence deliberately** — SCR-604's row says "tree" and MSP-54's list is flat |
| `categoryCoverage.noSupplier` / `noAward` | No supplier / No award | بلا مورد / بلا ترسية | authored |
| `categoryCoverage.fields.*` | Category / Approved suppliers / Active suppliers / Offerings / Tenders / Awarded | الفئة / موردون معتمدون / موردون فعّالون / العروض / الطلبات / الطلبات المُرساة | «معتمدون» vs «فعّالون» is the approved-versus-can-trade distinction the two columns exist for |
| `notificationPreferences.title` | Notification Preferences | تفضيلات التنبيهات | SCR-901 |
| `notificationPreferences.subtitle` | Choose which optional notifications you want switched off. | اختر التنبيهات الاختيارية التي تريد إيقافها. | authored |
| `notificationPreferences.optional` / `optionalHint` | Optional notifications / Unticking one stops it reaching you. | تنبيهات اختيارية / إلغاء التحديد يوقف وصول هذا التنبيه إليك. | authored |
| `notificationPreferences.alwaysOn` | Notifications that cannot be switched off | تنبيهات لا يمكن إيقافها | D-60 |
| `notificationPreferences.alwaysOnHint` | Invitations, clarification requests, award outcomes and document expiry are always sent. | الدعوات وطلبات الاستيضاح ونتائج الترسية وانتهاء المستندات تُرسل دائماً. | **names D-60's four families verbatim** — this line is the ruling as a user reads it |
| `notificationPreferences.saved` / `saveFailed` / `loadFailed` / `unsaved` | Preferences saved / Could not save your preferences / Could not load your preferences / Unsaved changes | تم حفظ التفضيلات / تعذّر حفظ التفضيلات / تعذّر تحميل التفضيلات / تغييرات غير محفوظة | same «تعذّر» register as the other failures |

**No Arabic was drafted for the 32 notification names,** and that is deliberate: SCR-901 renders each type's
own title from the notification copy catalogue — including an administrator's SCR-717 rewording — rather than
a second set of 32 labels in `i18n/config.ts`. The words a user recognises are the words they were sent, and a
second copy would drift from the first the day somebody reworded a template.

### One string corrected during the marker pass, not just unmarked

`referenceAdmin.awardCriticalExplained` told an administrator, in both languages, that **no document type
is marked award-critical** — on the very screen whose toggle does the marking. That stopped being true in
phase 1, when D-58 marked the commercial register and the tax certificate and BRULE-023 began firing. Both
strings now name the two types and say that changing the list is the Ministry's decision.

| Key | English | Arabic (authored) | Note |
|---|---|---|---|
| `referenceAdmin.awardCriticalExplained` | When an award-critical document expires, the supplier is suspended automatically (BRULE-023). The commercial register and the tax certificate are marked (D-58); changing that list is a ministry decision. | انتهاء صلاحية مستند موسوم كحرج للترسية يُعلّق المورد تلقائياً (BRULE-023). الموسومان حالياً هما السجل التجاري والشهادة الضريبية (D-58)؛ وأي تغيير في هذه القائمة قرار يخص الوزارة. | Corrected, not merely unmarked — the previous text was stale in a way that would have told an administrator the rule was dormant while it was suspending suppliers |

### D-66 · SCR-601, 602, 603 and 606

The four Ministry screens, built under D-66. Three word choices carry the weight: «رصد» for the monitor —
watching rather than managing, which is what a read-only oversight screen does; «سجل» for the registry, the
register itself; and «الإنفاق» for spend, the word a budget document uses.

| Key | English | Arabic (authored) | Note |
|---|---|---|---|
| `ministryRfqs.title` | Tender Monitor | رصد المناقصات | «رصد» is observation, not «إدارة» — this persona cannot act on anything |
| `ministryRfqs.subtitle` | Every tender across all buying bodies, with the awarding organisation and, where there is one, the award value. | كل الطلبات على مستوى الجهات، مع الجهة المشترية وقيمة الترسية حيثما وُجدت. | «حيثما وُجدت» keeps "where there is one" — an unawarded tender has no value, which is not the same as a withheld one |
| `ministryRfqs.fields.bids` | Bids / invited | العروض/المدعوون | Two counts in one column, as the screen renders them |
| `ministryRfqDetail.readOnly` | Read-only | اطّلاع فقط | Literally "viewing only"; «للقراءة فقط» reads as a file attribute |
| `ministryRfqDetail.valuesWithheld` | Commercial values are currently withheld by disclosure policy; counts are shown. | القيم المالية محجوبة حالياً بحسب سياسة الإفصاح؛ الأعداد معروضة. | **The sentence the flag exists for.** «محجوبة» = withheld by a decision, not «غير متوفرة» = unavailable — the difference between policy and absence |
| `ministryRfqDetail.notAwarded` / `withheld` | Not awarded yet / Withheld | لم تُرسَ بعد / غير متاح | Kept distinct on purpose: both render where a number would go and they mean opposite things |
| `ministryRfqDetail.awarded` | Winning bid | العرض الفائز | |
| `ministrySuppliers.title` | National Supplier Registry | سجل الموردين الوطني | «الوطني» distinguishes it from SCR-307's reviewer register, which is the same word without it |
| `ministryAwards.title` | Awards & Spend | الترسيات والإنفاق | |
| `ministryAwards.categoryNote` | An award counts once per category its tender touched, so this column can total more than the number of awards. | تُحتسب الترسية مرة واحدة لكل فئة يشملها الطلب، لذا قد يتجاوز مجموع العمود عدد الترسيات. | Says why the column does not add up, rather than letting a reader add it up and be wrong |
| `ministryAwards.valuesWithheld` | Commercial values are currently withheld by disclosure policy; award counts are shown. | القيم المالية محجوبة حالياً بحسب سياسة الإفصاح؛ أعداد الترسيات معروضة. | Same «محجوبة» as above |

### BRULE-061 · the justification field on the scoring form

Five strings for the comment a criterion can require. «تبرير» is the word a procurement file uses for the
reason behind a decision; «تعليق» would read as a remark rather than as evidence.

| Key | English | Arabic (authored) | Note |
|---|---|---|---|
| `evaluation.my.justification` | Justification | التبرير | The field's accessible name, composed with the criterion's own name |
| `evaluation.my.justificationRequired` | Justification required | التبرير مطلوب | Badge beside a criterion that carries the flag |
| `evaluation.my.justificationPlaceholder` | Optional justification | تبرير اختياري | |
| `evaluation.my.justificationPlaceholderRequired` | Say why you gave this score | اكتب تبريرك لهذه الدرجة | Imperative, addressed to the evaluator, as UX-WRITING §10 asks of an instruction |
| `evaluation.my.justificationMissing` | This criterion requires a justification before the score can be saved. | هذا المعيار يتطلب تبريراً قبل حفظ الدرجة. | States the rule and its consequence in one line - what is blocked, and what unblocks it |

Two keys were REMOVED in the same pass: `evaluation.my.commentAr` and `evaluation.my.commentEn`, which
labelled a two-language comment pair no screen ever rendered. BRULE-061 takes either language and asks
for no translation, so the pair was never the right shape.

### The design-token pass · one shared failure string

Eighteen screens rendered their EMPTY state when a fetch failed. The fix is one component and one string
rather than eighteen variants, so this section is short on purpose: the words a person needs when a screen
cannot load are the same on every screen, and eighteen wordings would be eighteen things to keep aligned.

| Key | English | Arabic | Note |
|---|---|---|---|
| `common.loadFailed` | We could not load this. Try again. | تعذّر تحميل هذه البيانات. حاول مرة أخرى. | Authored. «تعذّر» is "could not", impersonal, rather than «فشل» (failed), which reads as a fault the reader caused. Two short sentences, matching §10's shape: what happened, then what to do |
| `common.retry` | Try again | إعادة المحاولة | **[reused]** - the same string this catalogue already carries in five places for the same action |


### The copy pass (Phase 5 of the redesign sequence) · three Arabic strings

The audit's §C4 listed 23 jargon items and §C2 five label-to-behaviour mismatches. **Most of them turned
out to be English-only defects**: on `threshold`, `incoterm`, `consolidate`, `recuse`, `evaluatorUserId`
and the supplier's own name for a tender, the Arabic already said the plain thing and the English carried
the acronym, the Latin term or a self-contradiction. Those were fixed on the English side alone, so they
add nothing to this file.

Three strings did need Arabic, and they ship on D-65's terms: **accepted for the demonstration build
without a line-by-line read**, not reviewed.

| Key | English | Arabic (authored) | Why it changed |
|---|---|---|---|
| `rfq.boundTemplate` | Evaluation template attached (version {{version}}) | قالب التقييم مرتبط (الإصدار {{version}}) | Both languages rendered a raw GUID to the reader through `{{id}}`. The identifier is gone from the sentence; the interpolation is simply no longer used |
| `proposal.clarificationHint` | Your response goes to the procurement officer, who decides whether to return the proposal to review. Proposal lines cannot be edited at this stage. | يُرسَل ردّك إلى موظف المشتريات، وهو من يقرّر إعادة العرض إلى المراجعة. لا يمكن تعديل بنود العرض في هذه المرحلة. | The old string said recording a response returns the proposal for re-review. It does not: the call sets `Revised`, and returning it to review is the officer's own separate action. The sibling string two lines down already said this correctly, so the screen contradicted itself |
| `notificationPreferences.alwaysOnHint` | Invitations, clarification requests and award outcomes are always sent and are not listed below. Document expiry reminders are always sent too, by email. | الدعوات وطلبات الاستيضاح ونتائج الترسية تُرسل دائماً ولا تظهر في القائمة أدناه. وتنبيهات انتهاء المستندات تُرسل دائماً أيضاً، بالبريد الإلكتروني. | All four families really are always sent, so the old line was true. What it did not say is that document expiry has no notification type behind it — it is an email reminder — so a reader looked for it in the list directly below the hint and could not find it |

### Plan 6A · the product fixes · two Arabic strings

Both ship on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) | Why it exists |
|---|---|---|---|
| `proposal.withdrawWarning` | Withdrawing is final. You cannot re-enter this tender, and a withdrawn proposal cannot be restored. | السحب نهائي. لا يمكنك العودة إلى هذه المناقصة، ولا يمكن استرجاع العرض بعد سحبه. | `Withdrawn` is terminal in the domain and the control was the lowest-emphasis variant in the system, with nothing anywhere saying so |
| `rfq.ownership.approverHint` | Applied when you submit for review. Leave blank to let any manager approve. | يُطبَّق عند الإرسال للمراجعة. اتركه فارغاً ليتمكن أي مدير من الاعتماد. | The approver select is committed by the button beside it, not by itself. The placeholder already said what blank means; nothing said which button applies the choice |

Four keys were **removed** in the same pass, in both languages: `rfq.manualCloseReason` (the fixed sentence
every early close used to send, which satisfied the audit rule while defeating it, replaced by a typed
reason), `proposal.withdrawReasonPlaceholder` (the inline field is now a dialog), and
`evaluation.evaluatorUserId` (it labelled a free-text GUID box that had already become a picker). Ten more
went with the shared error component: `account.errors.loadFailed`, `account.retry`,
`profile.errors.loadFailed`, `profile.retry`, `documents.errors.loadFailed`, `documents.retry`,
`notifications.loadFailed`, `notifications.retry`, `supplierDashboard.loadFailed`,
`supplierDashboard.retry`.

### Plan 6B · the cancel warning

Ships on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) | Why it exists |
|---|---|---|---|
| `rfq.cancelWarning` | Cancelling is final. Invited suppliers are told the tender is cancelled, and it cannot be reopened. | الإلغاء نهائي. يُبلَّغ الموردون المدعوون بإلغاء الطلب، ولا يمكن إعادة فتحه. | §D1: cancelling a live tender was an inline reason field beside the lowest-emphasis button in the system, with nothing saying the action was irreversible |

**This entry is also how the key-parity gap was found.** The edit that added it wrote the Arabic and
failed silently on the English, and because `config.ts` sets `fallbackLng: 'ar'`, the English cancel
dialog rendered the Arabic sentence with every test still green. `src/frontend/src/i18n/keyParity.test.ts`
now compares the two key sets in both directions.

### Plan 6B · the four group names

Ship on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) |
|---|---|---|
| `rfq.groups.tender` | The tender | الطلب |
| `rfq.groups.suppliers` | Suppliers | الموردون |
| `rfq.groups.decisions` | Decisions | القرارات |
| `rfq.groups.managing` | Managing this tender | إدارة الطلب |

They name the four regions the buyer's tender screen is now divided into. «الطلب» rather than «المناقصة»
because the rest of this screen's Arabic already calls the thing الطلب, and one screen using two words for
one object is the defect the copy pass spent its time removing.

### Phase 6E · the charts

Ships on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) | Why it exists |
|---|---|---|---|
| `charts.nothingToPlot` | No figures available to chart. The table below carries what there is. | لا توجد أرقام قابلة للرسم. الجدول أدناه يعرض المتاح منها. | D-57 withholds commercial values outside a demonstration environment. A chart of nothing but withheld months must say so rather than draw bars of height zero, which would assert that nothing was awarded |

### Phase 6D · the onboarding submit gate

Ship on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) |
|---|---|---|
| `onboarding.gateTitle` | Before you can submit | قبل أن ترسل طلبك |
| `onboarding.gateReady` | Ready to submit | الطلب جاهز للإرسال |
| `onboarding.gateHelp` | You can submit as soon as these are done. Everything else you have entered is saved. | يمكنك الإرسال بمجرد اكتمال هذه العناصر. وكل ما أدخلته غير ذلك محفوظ. |
| `onboarding.gateBlocked` | Available once the items above are done. | يصبح الإرسال متاحاً بعد اكتمال العناصر أعلاه. |

No plural forms, deliberately. A count would need Arabic's six plural categories to say "3 things left",
and the list itself already shows how many there are.

### Phase 6D · the two onboarding save buttons

Ship on D-65's terms: **accepted for the demonstration build without a line-by-line read**.

| Key | English | Arabic (authored) |
|---|---|---|
| `onboarding.saveLegal` | Save legal information | حفظ المعلومات النظامية |
| `onboarding.saveProfile` | Save profile | حفظ الملف |

Both forms on this screen submitted with a button reading only «حفظ» / "Save". Visually the card each sits
in disambiguates them; for a screen-reader user tabbing through, two identical "Save" buttons on one
screen do not.
