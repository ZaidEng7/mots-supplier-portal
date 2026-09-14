// The shipped wording of every email, in Arabic and English.
//
// The bodies were hardcoded English. This mirrors the interface's own two-locale resource shape, one file with
// two locale keys, rather than inventing a second localisation scheme.
//
// The recipient's own language setting is the only source of locale, and it is threaded in at compose time. The
// sending interface itself stays locale-unaware: a real transport needs the already-rendered subject and body to
// send, not to make its own localisation choice, so that interface is untouched.
//
// An unrecognised or missing language renders Arabic rather than English, matching the setting's own default,
// the interface's fallback, and the product's Arabic-first positioning.
//
//
// WHAT INDIVIDUAL TEMPLATES DELIBERATELY WITHHOLD
//
// The invitation's deep link opens the supplier-facing tender page, which re-enforces the invitation check on the
// server. The link is a convenience rather than a bypass.
//
// A published clarification does not name the asker. Anonymity holds in the notification and not only on the
// screen.
//
// A cancellation never states the reason. A cancellation reached before an award has an internal buyer-side
// reason, which is not automatically supplier-facing content; the email says only that submissions are no longer
// being accepted.
//
// A submission receipt never includes pricing. It confirms that the submission happened; it does not restate the
// sealed financial envelope.
//
// A regret notice never names the winner and states no commercial figure.
//
// An answered clarification goes to the asker whether it was answered privately or published, because either way
// their own answer is now visible to them.

namespace MotsSupplierPortal.Infrastructure.Email;

public static class EmailTemplates
{
    private static bool IsEnglish(string? locale) => locale == "en";

    public static (string Subject, string Body) Verification(string? locale, string verifyUrl) =>
        IsEnglish(locale)
            ? ("Verify your MOTS Supplier Portal account",
               $"<p>Click to verify your email:</p><p><a href=\"{verifyUrl}\">{verifyUrl}</a></p>")
            : ("تفعيل حسابك في بوابة الموردين",
               $"<p>يرجى الضغط لتفعيل بريدك الإلكتروني:</p><p><a href=\"{verifyUrl}\">{verifyUrl}</a></p>");

    public static (string Subject, string Body) PasswordReset(string? locale, string resetUrl) =>
        IsEnglish(locale)
            ? ("Reset your MOTS Supplier Portal password",
               $"<p>Click to reset your password:</p><p><a href=\"{resetUrl}\">{resetUrl}</a></p>")
            : ("إعادة تعيين كلمة المرور",
               $"<p>يرجى الضغط لإعادة تعيين كلمة المرور:</p><p><a href=\"{resetUrl}\">{resetUrl}</a></p>");

    public static (string Subject, string Body) SupplierUserInvite(string? locale, string acceptUrl) =>
        IsEnglish(locale)
            ? ("You've been invited to the MOTS Supplier Portal",
               "<p>You've been invited to join your organization's supplier account. Click to set your " +
               $"password and get started:</p><p><a href=\"{acceptUrl}\">{acceptUrl}</a></p>")
            : ("تمت دعوتك للانضمام إلى بوابة الموردين",
               "<p>تمت دعوتك للانضمام إلى حساب المورد الخاص بمؤسستك. يرجى الضغط لتعيين كلمة المرور " +
               $"والبدء:</p><p><a href=\"{acceptUrl}\">{acceptUrl}</a></p>");

    public static (string Subject, string Body) StaffInvite(string? locale, string acceptUrl) =>
        IsEnglish(locale)
            ? ("You've been invited to the MOTS Supplier Portal back office",
               "<p>You've been invited to join the MOTS Supplier Portal back office. Click to set " +
               $"your password and get started:</p><p><a href=\"{acceptUrl}\">{acceptUrl}</a></p>")
            : ("تمت دعوتك للانضمام إلى الإدارة الداخلية لبوابة الموردين",
               "<p>تمت دعوتك للانضمام إلى الإدارة الداخلية لبوابة الموردين. يرجى الضغط لتعيين كلمة " +
               $"المرور والبدء:</p><p><a href=\"{acceptUrl}\">{acceptUrl}</a></p>");

    public static (string Subject, string Body) AlreadyRegisteredNotice(string? locale, string publicUrl) =>
        IsEnglish(locale)
            ? ("You already have a MOTS Supplier Portal account",
               "<p>Someone just tried to register a new MOTS Supplier Portal account using this email " +
               "address (or your organization's registration number), but you already have one.</p>" +
               $"<p>If this was you, you can <a href=\"{publicUrl}/login\">sign in here</a>.</p>" +
               "<p>If you don't recognize this, no action is needed - your account is unaffected.</p>")
            : ("لديك بالفعل حساب في بوابة الموردين",
               "<p>حاول أحدهم للتو تسجيل حساب جديد في بوابة الموردين باستخدام بريدك الإلكتروني " +
               "(أو رقم تسجيل مؤسستك)، لكن لديك حساباً بالفعل.</p>" +
               $"<p>إذا كنت أنت من قام بذلك، يمكنك <a href=\"{publicUrl}/login\">تسجيل الدخول من هنا</a>.</p>" +
               "<p>إذا لم تتعرف على هذا الطلب، لا داعي لاتخاذ أي إجراء - حسابك غير متأثر.</p>");

    public static (string Subject, string Body) ApplicationApproved(string? locale) =>
        IsEnglish(locale)
            ? ("Your supplier application has been approved",
               "<p>Congratulations - your supplier application has been approved and your account is now Active.</p>")
            : ("تمت الموافقة على طلبك",
               "<p>تهانينا - تمت الموافقة على طلب انضمامك كمورد، وأصبح حسابك الآن نشطاً.</p>");

    public static (string Subject, string Body) ApplicationRejected(string? locale, string reason) =>
        IsEnglish(locale)
            ? ("Your supplier application was not approved",
               $"<p>Your supplier application was rejected for the following reason:</p><p>{reason}</p>" +
               "<p>You may correct the issue and register again.</p>")
            : ("لم تتم الموافقة على طلبك",
               $"<p>تم رفض طلب انضمامك كمورد للسبب التالي:</p><p>{reason}</p>" +
               "<p>يمكنك تصحيح المشكلة والتسجيل مرة أخرى.</p>");

    public static (string Subject, string Body) RfqInvitation(string? locale, string referenceCode, string rfqTitle, string deepLink) =>
        IsEnglish(locale)
            ? ($"You've been invited to RFQ {referenceCode}",
               $"<p>You've been invited to submit a proposal for <strong>{rfqTitle}</strong> ({referenceCode}).</p>" +
               $"<p><a href=\"{deepLink}\">View the RFQ</a></p>")
            : ($"تمت دعوتك لتقديم عرض على {referenceCode}",
               $"<p>تمت دعوتك لتقديم عرض على <strong>{rfqTitle}</strong> ({referenceCode}).</p>" +
               $"<p><a href=\"{deepLink}\">عرض الطلب</a></p>");

    public static (string Subject, string Body) ClarificationAnswered(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"Your question on {referenceCode} has been answered",
               $"<p>The buyer has answered your clarification question on {referenceCode}.</p>")
            : ($"تمت الإجابة على سؤالك بخصوص {referenceCode}",
               $"<p>أجاب المشتري على سؤال الاستيضاح الخاص بك بخصوص {referenceCode}.</p>");

    public static (string Subject, string Body) ClarificationPublished(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"New published clarification on {referenceCode}",
               $"<p>A clarification question and answer has been published to all invited suppliers on {referenceCode}.</p>")
            : ($"استيضاح جديد منشور بخصوص {referenceCode}",
               $"<p>تم نشر سؤال وجواب استيضاح لجميع الموردين المدعوين بخصوص {referenceCode}.</p>");

    public static (string Subject, string Body) ClarificationPosted(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"New clarification question on {referenceCode}",
               $"<p>An invited supplier has posted a clarification question on {referenceCode}.</p>")
            : ($"سؤال استيضاح جديد بخصوص {referenceCode}",
               $"<p>قام أحد الموردين المدعوين بطرح سؤال استيضاح بخصوص {referenceCode}.</p>");

    public static (string Subject, string Body) RfqAddendum(string? locale, string referenceCode, string addendumTitle) =>
        IsEnglish(locale)
            ? ($"Addendum issued on {referenceCode}",
               $"<p>An addendum has been issued on {referenceCode}: <strong>{addendumTitle}</strong>.</p>")
            : ($"تم إصدار ملحق بخصوص {referenceCode}",
               $"<p>تم إصدار ملحق بخصوص {referenceCode}: <strong>{addendumTitle}</strong>.</p>");

    public static (string Subject, string Body) RfqPublished(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"{referenceCode} is now open for submissions",
               $"<p>{referenceCode} has been published and is now open for proposal submissions.</p>")
            : ($"طلب عرض السعر {referenceCode} أصبح مفتوحاً لتقديم العروض",
               $"<p>تم نشر طلب عرض السعر {referenceCode} وأصبح مفتوحاً لتقديم العروض.</p>");

    public static (string Subject, string Body) RfqCancelled(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"{referenceCode} has been cancelled",
               $"<p>{referenceCode} has been cancelled. No further action is required on your part.</p>")
            : ($"تم إلغاء طلب عرض السعر {referenceCode}",
               $"<p>تم إلغاء طلب عرض السعر {referenceCode}. لا حاجة لأي إجراء إضافي من جانبكم.</p>");

    public static (string Subject, string Body) EvaluatorAssigned(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"You have been assigned to evaluate {referenceCode}",
               $"<p>You have been assigned as an evaluator for {referenceCode}. Please log in to begin scoring.</p>")
            : ($"تم تعيينك لتقييم طلب عرض السعر {referenceCode}",
               $"<p>تم تعيينك كمقيّم لطلب عرض السعر {referenceCode}. يرجى تسجيل الدخول لبدء عملية التقييم.</p>");

    public static (string Subject, string Body) ProposalSubmitted(string? locale, string proposalReferenceCode, string rfqReferenceCode) =>
        IsEnglish(locale)
            ? ($"Proposal {proposalReferenceCode} submitted",
               $"<p>Your proposal ({proposalReferenceCode}) for {rfqReferenceCode} has been submitted successfully.</p>")
            : ($"تم إرسال العرض {proposalReferenceCode}",
               $"<p>تم إرسال عرضك ({proposalReferenceCode}) الخاص بـ {rfqReferenceCode} بنجاح.</p>");

    public static (string Subject, string Body) AwardIssued(string? locale, string rfqReferenceCode) =>
        IsEnglish(locale)
            ? ($"You have been awarded {rfqReferenceCode}",
               $"<p>Congratulations - your proposal for {rfqReferenceCode} has been awarded. Please log in for details.</p>")
            : ($"تمت ترسية {rfqReferenceCode} عليكم",
               $"<p>تهانينا - تمت ترسية طلب عرض السعر {rfqReferenceCode} على عرضكم. يرجى تسجيل الدخول للاطلاع على التفاصيل.</p>");

    public static (string Subject, string Body) AwardRegret(string? locale, string rfqReferenceCode) =>
        IsEnglish(locale)
            ? ($"Outcome for {rfqReferenceCode}",
               $"<p>Thank you for your proposal for {rfqReferenceCode}. On this occasion, your proposal was not selected for award.</p>")
            : ($"نتيجة طلب عرض السعر {rfqReferenceCode}",
               $"<p>شكراً لتقديمكم عرضاً بخصوص {rfqReferenceCode}. لم يقع الاختيار على عرضكم هذه المرة.</p>");

    public static (string Subject, string Body) InfoRequested(string? locale, string reason) =>
        IsEnglish(locale)
            ? ("Action needed on your supplier application",
               $"<p>The reviewer has requested more information:</p><p>{reason}</p>" +
               "<p>Please log in to address the flagged items and resubmit.</p>")
            : ("مطلوب إجراء بخصوص طلبك",
               $"<p>طلب المراجع مزيداً من المعلومات:</p><p>{reason}</p>" +
               "<p>يرجى تسجيل الدخول لمعالجة الملاحظات وإعادة الإرسال.</p>");

    public static (string Subject, string Body) ApplicationResubmitted(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"Supplier application {referenceCode} resubmitted",
               $"<p>Supplier application {referenceCode} has addressed the flagged items and been " +
               "resubmitted for review.</p>")
            : ($"تمت إعادة تقديم طلب المورد {referenceCode}",
               $"<p>عالج طلب المورد {referenceCode} الملاحظات المطلوبة وأُعيد تقديمه للمراجعة.</p>");

    public static (string Subject, string Body) DocumentRejected(string? locale, string fileName, string? reason) =>
        IsEnglish(locale)
            ? ("A document on your supplier profile was rejected",
               $"<p>Your document \"{fileName}\" was rejected for the following reason:</p><p>{reason}</p>" +
               "<p>Please correct the issue and re-upload it.</p>")
            : ("تم رفض أحد المستندات في ملفك",
               $"<p>تم رفض المستند \"{fileName}\" للسبب التالي:</p><p>{reason}</p>" +
               "<p>يرجى تصحيح المشكلة وإعادة رفعه.</p>");

    public static (string Subject, string Body) DocumentExpiring(string? locale, string fileName) =>
        IsEnglish(locale)
            ? ("A document on your supplier profile is expiring soon",
               $"<p>Your document \"{fileName}\" will expire soon. Please renew and re-upload it.</p>")
            : ("أحد المستندات في ملفك على وشك الانتهاء",
               $"<p>المستند \"{fileName}\" على وشك الانتهاء. يرجى تجديده وإعادة رفعه.</p>");

    public static (string Subject, string Body) DocumentExpired(string? locale, string fileName) =>
        IsEnglish(locale)
            ? ("A document on your supplier profile has expired",
               $"<p>Your document \"{fileName}\" has expired and your profile is now flagged incomplete. " +
               "Please re-upload it.</p>")
            : ("انتهت صلاحية أحد المستندات في ملفك",
               $"<p>انتهت صلاحية المستند \"{fileName}\" وأصبح ملفك الآن غير مكتمل. يرجى إعادة رفعه.</p>");
}
