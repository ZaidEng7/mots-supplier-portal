# UX Writing & Content — MOTS Supplier Portal

> **Status:** Baseline v1 · **Owner:** Design Lead / Content · **Date:** 2026-08-26
> Bilingual (Arabic-first) voice, microcopy patterns, and a reusable EN/AR message library. Consistent
> with [`00-foundational-decisions.md`](../architecture/00-foundational-decisions.md) (§5 state
> machines, §8 localization) and the [Discovery Report](../product/DISCOVERY-REPORT.md). Companions:
> [`UX-PRINCIPLES.md`](./UX-PRINCIPLES.md) · [`DESIGN-SYSTEM.md`](./DESIGN-SYSTEM.md) ·
> [`RESPONSIVE-AND-RTL.md`](./RESPONSIVE-AND-RTL.md) · [`ACCESSIBILITY.md`](./ACCESSIBILITY.md)

Every user-facing string is **i18next-keyed** (foundational §8) — no hard-coded text. Arabic is the
primary source of truth; English is the secondary reference. Copy is **task-oriented**, never
data-model-oriented (UX-Principles anti-pattern 5.2).

---

## 1. Voice & tone

### 1.1 Voice (constant)
The portal sounds like a **trustworthy institutional partner**: clear, calm, respectful, competent.
It is a public-procurement system — credibility over cleverness.

| We are | We are not |
|---|---|
| Clear & plain | Jargon-heavy or bureaucratic |
| Respectful & professional | Casual, jokey, or slangy |
| Confident & precise | Vague or hedging |
| Helpful & guiding | Condescending or blaming |
| Honest (incl. about failures) | Evasive; hiding errors |
| Concise | Verbose or padded |

### 1.2 Tone (varies by moment)

| Situation | Tone | Example (EN) |
|---|---|---|
| Routine action | Neutral, efficient | "Proposal saved." |
| Success / milestone | Warm, affirming (restrained) | "Your proposal was submitted." |
| Error (user-recoverable) | Calm, solution-first, never blaming | "That file is too large. Upload a file under 10 MB." |
| Error (system) | Honest, reassuring, actionable | "We couldn't save just now. Your changes are kept — try again." |
| Destructive confirm | Serious, precise | "This will cancel the RFQ for all invited suppliers. This can't be undone." |
| Empty state | Encouraging, guiding | "No RFQs yet. Create your first RFQ to invite suppliers." |
| Award moment | Dignified, significant | "Award approved for Supplier X." |

### 1.3 Bilingual principles

- **Arabic-first authoring:** write the Arabic string with native fluency (Modern Standard Arabic,
  professional register), then the English equivalent — not a literal translation of English.
- **Formality:** Arabic uses respectful professional register; avoid overly literary or overly colloquial.
  Prefer clear MSA understandable across Arabic speakers.
- **Consistency:** maintain a bilingual **glossary** of domain terms (RFQ, Proposal, Evaluation, Award,
  Onboarding, Supplier, Clarification) so the same concept always uses the same term. See §8.
- **No mixed-script leakage** unless intentional (entity codes, brand): codes/numbers are bidi-isolated
  (`<bdi>`) per [RTL §5.3](./RESPONSIVE-AND-RTL.md).
- **Length:** Arabic and English differ in length — copy and layouts must tolerate ~30% variance; never
  truncate meaning. Test both in the DS matrix.
- **Numerals/dates/currency:** formatted per [RTL §6](./RESPONSIVE-AND-RTL.md) (Western digits default,
  SYP default, Gregorian default).

---

## 2. Microcopy patterns

- **Labels:** noun or short phrase, sentence case (EN), no trailing colon in the label element.
  ("Company name / اسم الشركة").
- **Buttons:** **verb-first**, specific to the action, 1–3 words. "Submit proposal" not "OK". Avoid
  "Submit" alone where the object matters.
- **Helper text:** persistent, format/why guidance. ("We'll send verification here / سنرسل رمز التحقق
  إلى هذا البريد").
- **Placeholders:** example format only, never the label, never essential info.
- **Tooltips:** short clarifications for icons/terms; never the only place critical info lives.
- **Section intros:** one line explaining purpose when a step's intent isn't obvious.
- **Counts/ranges:** "1–25 of 340" bilingual, numerals per locale.
- **Timestamps:** relative in feeds ("2 hours ago / قبل ساعتين"), absolute + timezone for deadlines.
- **Truncation:** only non-essential secondary text; never money, codes, or names.

---

## 3. Error message formula

Every error answers four questions in order (this is the mandated pattern — UX-Principles 2.5, 5.4;
[ACCESSIBILITY §7](./ACCESSIBILITY.md)):

> **1) What happened** → **2) What to do** → **3) Was my data saved?** → **4) Is it safe to retry?**

Rules: plain language, no codes/stack traces/enums in the primary message; never blame the user; put a
correlation ID behind a "details" affordance only. Field-level errors show inline (not toasts);
system-level errors show a banner/toast + retry.

### 3.1 Examples

| Scenario | Message (EN) | Message (AR) |
|---|---|---|
| File too large (field) | "This file is 14 MB. Upload a file under 10 MB. Your other files are kept." | «حجم هذا الملف ١٤ ميغابايت. الرجاء رفع ملف أقل من ١٠ ميغابايت. تم الاحتفاظ ببقية ملفاتك.» |
| Wrong file type | "Only PDF, JPG, or PNG are accepted for this document." | «يُقبل فقط PDF أو JPG أو PNG لهذه الوثيقة.» |
| Required field | "Enter the company legal name to continue." | «أدخل الاسم القانوني للشركة للمتابعة.» |
| Save failed (transient) | "We couldn't save just now. Your changes are kept on this page — try again in a moment. If it keeps happening, contact support." | «تعذّر الحفظ الآن. تم الاحتفاظ بتعديلاتك على هذه الصفحة — أعد المحاولة بعد لحظات. إذا استمرت المشكلة، تواصل مع الدعم.» |
| Submission after deadline | "The submission window for this RFQ closed on 30 Aug 2026, 14:00 (+03). You can no longer submit a proposal." | «انتهت مهلة التقديم لهذا الطلب في ٣٠ أغسطس ٢٠٢٦، الساعة ١٤:٠٠ (+٣). لم يعد بإمكانك تقديم عرض.» |
| Permission denied | "You don't have permission to publish this RFQ. Ask a Procurement Manager to approve it." | «لا تملك صلاحية نشر هذا الطلب. اطلب من مدير المشتريات اعتماده.» |
| Session expired | "Your session ended for security. Sign in again — your draft is saved." | «انتهت جلستك لأسباب أمنية. سجّل الدخول من جديد — تم حفظ مسودتك.» |
| ERP sync failed (info, non-blocking) | "The award is recorded. Sending it to the ERP is delayed and will retry automatically." | «تمت الترسية وتسجيلها. تأخّر إرسالها إلى نظام تخطيط الموارد وسيُعاد تلقائياً.» |
| Not found | "This RFQ doesn't exist or was removed." | «هذا الطلب غير موجود أو تمت إزالته.» |
| Concurrent edit (RowVersion) | "Someone updated this while you were editing. Review the latest version and reapply your changes." | «قام شخص آخر بتحديث هذا أثناء تعديلك. راجع أحدث نسخة وأعد تطبيق تغييراتك.» |

> The ERP-sync message reflects the **non-blocking async ERP boundary** (foundational §1): the portal
> never fails a core action because the ERP is unavailable.

---

## 4. Empty-state formula

> **Icon/illustration** + **Title (what this is)** + **One line (why it's empty / what it's for)** +
> **Primary action** (+ optional secondary/learn-more)

Distinguish **first-run empty** (guide to create) from **filtered/search empty** (offer to clear).

| Context | Title (EN) | Body (EN) | Action | AR title / action |
|---|---|---|---|---|
| Supplier — no proposals | "No proposals yet" | "When you respond to an RFQ invitation, your proposals appear here." | "Browse open RFQs" | «لا توجد عروض بعد» / «تصفح الطلبات المفتوحة» |
| Procurement — no RFQs | "No RFQs yet" | "Create your first RFQ to invite suppliers and collect proposals." | "Create RFQ" | «لا توجد طلبات بعد» / «إنشاء طلب» |
| Reviewer — empty queue | "You're all caught up" | "No supplier applications are waiting for review." | — | «أنجزت كل المهام» |
| Evaluator — nothing assigned | "Nothing to evaluate" | "Proposals assigned to you for scoring will appear here." | — | «لا يوجد ما يُقيَّم» |
| Documents — none uploaded | "No documents uploaded" | "Upload the required documents to complete your profile." | "Upload document" | «لم يتم رفع أي وثائق» / «رفع وثيقة» |
| Filtered table — no results | "No results" | "No items match your filters. Try clearing them." | "Clear filters" | «لا توجد نتائج» / «مسح عوامل التصفية» |
| Ministry — no data in range | "No data for this period" | "Try a different date range." | "Change range" | «لا توجد بيانات لهذه الفترة» / «تغيير الفترة» |

---

## 5. Confirmation copy

Tiered by risk (see [DESIGN-SYSTEM §6.18](./DESIGN-SYSTEM.md)). High-risk irreversible actions require
a **typed reason**; the reason is stored in `AuditLog` and shown thereafter (trust principle).

> **Title (the action + object)** + **Consequence (who/what is affected, reversibility)** +
> **[reason field if irreversible]** + **explicit action button (verb) / Cancel**

| Action | Title (EN) | Consequence (EN) | Confirm button | AR confirm |
|---|---|---|---|---|
| Cancel RFQ | "Cancel this RFQ?" | "This cancels RFQ-2026-000123 for all 12 invited suppliers. Submitted proposals will be closed. This can't be undone." (+ reason) | "Cancel RFQ" | «إلغاء الطلب» |
| Reject supplier | "Reject this application?" | "The supplier will be notified and must resubmit. Add a reason they'll see." (+ reason) | "Reject application" | «رفض الطلب» |
| Publish RFQ | "Publish this RFQ?" | "Invited suppliers will be notified and can submit proposals until the deadline." | "Publish" | «نشر» |
| Finalize evaluation | "Finalize evaluation?" | "Scores will be locked and consolidated. Evaluators can no longer change their scores." | "Finalize" | «إنهاء التقييم» |
| Approve award | "Approve this award?" | "This awards RFQ-2026-000123 to Supplier X and starts the purchase-order process." | "Approve award" | «اعتماد الترسية» |
| Withdraw proposal | "Withdraw your proposal?" | "Your proposal will be removed from this RFQ. You can submit a new one while the window is open." | "Withdraw" | «سحب العرض» |
| Deactivate supplier | "Deactivate this supplier?" | "They lose portal access and won't receive new invitations. You can reactivate later." (+ reason) | "Deactivate" | «إلغاء التفعيل» |
| Delete draft | "Delete this draft?" | "This draft will be permanently removed. This can't be undone." | "Delete draft" | «حذف المسودة» |

Low-risk reversible actions skip the dialog and use a **toast with Undo** ("Invitation removed — Undo /
تمت إزالة الدعوة — تراجع").

---

## 6. Button label catalogue

Verb-first, specific, consistent. Never "OK/Submit" where an object clarifies.

| Intent | EN | AR |
|---|---|---|
| Primary create | Create RFQ / Add supplier | إنشاء طلب / إضافة مورد |
| Save draft | Save draft | حفظ المسودة |
| Save & continue | Save and continue | حفظ ومتابعة |
| Submit for review | Submit for review | إرسال للمراجعة |
| Publish | Publish | نشر |
| Submit proposal | Submit proposal | تقديم العرض |
| Withdraw | Withdraw | سحب |
| Upload | Upload document | رفع وثيقة |
| Replace file | Replace | استبدال |
| Invite | Invite suppliers | دعوة موردين |
| Approve / Reject | Approve · Reject | اعتماد · رفض |
| Request info | Request info | طلب معلومات |
| Score / Submit scores | Submit scores | إرسال التقييم |
| Shortlist | Shortlist | إدراج في القائمة المختصرة |
| Award | Approve award | اعتماد الترسية |
| Cancel action (dialog) | Cancel | إلغاء |
| Dismiss / Close | Close | إغلاق |
| Retry | Try again | إعادة المحاولة |
| Undo | Undo | تراجع |
| Export | Export | تصدير |
| Sign in / out | Sign in · Sign out | تسجيل الدخول · تسجيل الخروج |

> "Cancel" (dismiss a dialog / إلغاء) and "Cancel RFQ" (a domain action / إلغاء الطلب) are distinct —
> never use bare "Cancel" for the destructive domain action.

---

## 7. Status labels (aligned to canonical state machines)

Exact labels for every domain state (foundational §5). These are the single source for chip text
([DESIGN-SYSTEM §6.15](./DESIGN-SYSTEM.md)) and for the accessible name announced to screen readers.

### 7.1 Supplier onboarding
| State | EN | AR |
|---|---|---|
| Draft | Draft | مسودة |
| EmailVerified | Email verified | تم التحقق من البريد |
| ProfileInProgress | In progress | قيد الإكمال |
| Submitted | Submitted | مُقدَّم |
| UnderReview | Under review | قيد المراجعة |
| InfoRequested | Info requested | مطلوب معلومات |
| Resubmitted | Resubmitted | أُعيد التقديم |
| Approved | Approved | معتمد |
| Rejected | Rejected | مرفوض |
| Active | Active | نشط |
| Suspended | Suspended | موقوف |
| Deactivated | Deactivated | مُلغى التفعيل |

### 7.2 Supplier document
| State | EN | AR |
|---|---|---|
| Required | Required | مطلوب |
| Uploaded | Uploaded | تم الرفع |
| UnderReview | Under review | قيد المراجعة |
| Approved | Approved | معتمد |
| Rejected | Rejected | مرفوض |
| ExpiringSoon | Expiring soon | ينتهي قريباً |
| Expired | Expired | منتهٍ |

### 7.3 RFQ
| State | EN | AR |
|---|---|---|
| Draft | Draft | مسودة |
| InternalReview | Internal review | مراجعة داخلية |
| Approved | Approved | معتمد |
| Published | Published | منشور |
| SubmissionOpen | Open for submissions | مفتوح للتقديم |
| SubmissionClosed | Submissions closed | أُغلق التقديم |
| UnderEvaluation | Under evaluation | قيد التقييم |
| Clarification | Clarification | استيضاح |
| Shortlisting | Shortlisting | إعداد القائمة المختصرة |
| Recommendation | Recommendation | توصية |
| AwardApproval | Award approval | اعتماد الترسية |
| Awarded | Awarded | تمت الترسية |
| Completed | Completed | مكتمل |
| Cancelled | Cancelled | ملغى |

### 7.4 Proposal
| State | EN | AR |
|---|---|---|
| Draft | Draft | مسودة |
| Submitted | Submitted | مُقدَّم |
| UnderReview | Under review | قيد المراجعة |
| ClarificationRequested | Clarification requested | مطلوب استيضاح |
| Revised | Revised | مُعدَّل |
| Shortlisted | Shortlisted | ضمن القائمة المختصرة |
| NotSelected | Not selected | غير مختار |
| AwardOffered | Award offered | عرض ترسية |
| Awarded | Awarded | تمت الترسية |
| Declined | Declined | مرفوض من المورد |
| Withdrawn | Withdrawn | مسحوب |

### 7.5 Evaluation
| State | EN | AR |
|---|---|---|
| NotStarted | Not started | لم يبدأ |
| Assigned | Assigned | مُسند |
| InProgress | In progress | قيد التنفيذ |
| EvaluatorSubmitted | Submitted | تم الإرسال |
| Consolidated | Consolidated | مُجمَّع |
| Finalized | Finalized | نهائي |

### 7.6 Award / Approval & ERP sync
| State | EN | AR |
|---|---|---|
| Recommended | Recommended | موصى به |
| PendingApproval | Pending approval | بانتظار الاعتماد |
| Approved | Approved | معتمد |
| Rejected | Rejected | مرفوض |
| Awarded | Awarded | تمت الترسية |
| Sync pending | Sync pending | بانتظار المزامنة |
| Synced | Synced | تمت المزامنة |
| Sync failed | Sync failed | فشل المزامنة |

---

## 8. Bilingual glossary (domain terms — use consistently everywhere)

| Concept | EN | AR |
|---|---|---|
| Supplier | Supplier | مورد |
| Buying entity / Organization | Buying entity | جهة شرائية |
| Onboarding | Onboarding | التسجيل والاعتماد |
| RFQ (Request for Quotation) | RFQ | طلب عرض أسعار |
| Invitation | Invitation | دعوة |
| Proposal | Proposal | عرض |
| Clarification (Q&A) | Clarification | استيضاح |
| Evaluation | Evaluation | تقييم |
| Criterion / weighted criteria | Criteria | معايير |
| Shortlist | Shortlist | القائمة المختصرة |
| Recommendation | Recommendation | توصية |
| Award | Award | ترسية |
| Purchase Order (ERP) | Purchase order | أمر شراء |
| Document | Document | وثيقة |
| Category | Category | فئة |
| Offering | Offering | عرض المنتجات/الخدمات |
| Audit trail | Audit trail | سجل التدقيق |
| Ministry of Tourism | Ministry of Tourism | وزارة السياحة |

---

## 9. Reusable message-pattern library (EN / AR)

Drop-in patterns keyed for i18next. `{tokens}` are interpolated (locale-formatted numerals/dates/
currency, bidi-isolated codes).

| Key | Pattern (EN) | Pattern (AR) |
|---|---|---|
| `save.success` | Saved | تم الحفظ |
| `save.autosaving` | Saving… | جارٍ الحفظ… |
| `save.saved_at` | Saved · {time} | تم الحفظ · {time} |
| `submit.success` | {object} submitted | تم تقديم {object} |
| `action.undo_toast` | {object} {action} — Undo | {action} {object} — تراجع |
| `deadline.countdown` | {duration} left to submit | متبقٍ {duration} للتقديم |
| `deadline.passed` | Submission window closed on {datetime} | انتهت مهلة التقديم في {datetime} |
| `validation.required` | Enter {field} to continue | أدخل {field} للمتابعة |
| `validation.range` | {field} must be between {min} and {max} | يجب أن يكون {field} بين {min} و{max} |
| `error.generic_retry` | Something went wrong. Your changes are kept — try again. | حدث خطأ ما. تم الاحتفاظ بتعديلاتك — أعد المحاولة. |
| `error.permission` | You don't have permission to {action}. | لا تملك صلاحية {action}. |
| `error.not_found` | {object} doesn't exist or was removed. | {object} غير موجود أو تمت إزالته. |
| `error.session_expired` | Your session ended. Sign in again — your draft is saved. | انتهت جلستك. سجّل الدخول من جديد — تم حفظ مسودتك. |
| `sync.pending` | {object} recorded. Syncing to the ERP will retry automatically. | تم تسجيل {object}. ستُعاد المزامنة مع النظام تلقائياً. |
| `confirm.irreversible` | This can't be undone. | لا يمكن التراجع عن هذا الإجراء. |
| `confirm.reason_required` | Add a reason (the {audience} will see this). | أضف سبباً (سيظهر لـ{audience}). |
| `empty.first_run` | No {object} yet. {cta} | لا يوجد {object} بعد. {cta} |
| `empty.filtered` | No results match your filters. | لا توجد نتائج مطابقة لعوامل التصفية. |
| `table.range` | {start}–{end} of {total} | {start}–{end} من {total} |
| `notif.new_invitation` | You've been invited to RFQ {code} — {title}. Respond by {datetime}. | تمت دعوتك للطلب {code} — {title}. قدّم ردك قبل {datetime}. |
| `notif.doc_expiring` | Your {docType} expires on {date}. Renew it to stay compliant. | تنتهي صلاحية {docType} في {date}. جدّدها للحفاظ على الامتثال. |
| `notif.proposal_shortlisted` | Your proposal for RFQ {code} was shortlisted. | تم إدراج عرضك للطلب {code} في القائمة المختصرة. |
| `notif.award` | RFQ {code} was awarded to {supplier}. | تمت ترسية الطلب {code} على {supplier}. |
| `notif.info_requested` | More information is needed for your application. | مطلوب معلومات إضافية بخصوص طلبك. |

---

## 10. Notification copy principles

Mirrors the `Notification` aggregate; delivered in-app (bell panel) and via channels. Each notification:

- **Leads with what happened + which object** (bidi-isolated code), then **why it matters / what to do**,
  then a **direct link** to act.
- Actionable and time-aware (include deadlines with timezone).
- Bilingual per the user's locale; numerals/dates formatted per [RTL §6](./RESPONSIVE-AND-RTL.md).
- Never leaks data across scope (RBAC §6): suppliers see only their own; ministry sees governance-level.
- Grouped by day, read/unread state, non-intrusive; critical ones (deadline imminent, document expired)
  get higher prominence but never alarmist tone.

---

## 11. Writing checklist (per string)

- [ ] i18next-keyed; **Arabic authored natively**, English equivalent (not literal).
- [ ] Task-oriented; no raw enums, IDs, or model names.
- [ ] Buttons verb-first and specific.
- [ ] Errors follow the four-part formula (what / do / saved? / retry?), never blame.
- [ ] Confirmations state consequence + reversibility; reason field on irreversible.
- [ ] Status labels match §7 exactly (chip text = SR accessible name).
- [ ] Domain terms match the glossary §8.
- [ ] Numerals/dates/currency locale-formatted; codes bidi-isolated.
- [ ] Fits both AR/EN lengths without truncating meaning.
