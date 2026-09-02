namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// MSP-69: the 11 previously-hardcoded-English email bodies (EmailJobs.cs), in ar/en pairs.
/// Mirrors the frontend's own ar/en resource shape (src/frontend/src/i18n/config.ts) - one file,
/// two locale keys - rather than inventing a second localization scheme (.resx, per-locale JSON
/// files, a resource-key indirection layer). AppUser.Language is the only locale source (already
/// existed on the entity, default "ar", matching i18n/config.ts's fallbackLng); it is threaded in
/// from EmailJobs at compose time. IEmailSender itself stays locale-unaware - a real transport
/// (EPIC-15) needs the already-rendered subject/body to send, not to make its own localization
/// choice, so the interface is untouched.
///
/// <para>Unrecognized or missing locale values render Arabic, not English - matching both
/// AppUser.Language's own default and the frontend's fallbackLng, and the product's Arabic-first
/// positioning (docs/ux/RESPONSIVE-AND-RTL.md).</para>
/// </summary>
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

    /// <summary>FEAT-08.3/FR-INV-003: the deep link opens the supplier-facing RFQ detail page,
    /// which itself re-enforces the Invitation check server-side (FEAT-08.6) - the link is a
    /// convenience, not a bypass.</summary>
    public static (string Subject, string Body) RfqInvitation(string? locale, string referenceCode, string rfqTitle, string deepLink) =>
        IsEnglish(locale)
            ? ($"You've been invited to RFQ {referenceCode}",
               $"<p>You've been invited to submit a proposal for <strong>{rfqTitle}</strong> ({referenceCode}).</p>" +
               $"<p><a href=\"{deepLink}\">View the RFQ</a></p>")
            : ($"تمت دعوتك لتقديم عرض على {referenceCode}",
               $"<p>تمت دعوتك لتقديم عرض على <strong>{rfqTitle}</strong> ({referenceCode}).</p>" +
               $"<p><a href=\"{deepLink}\">عرض الطلب</a></p>");

    /// <summary>FEAT-10.6/FR-CLR-006: sent to the asker once their question has an answer,
    /// regardless of whether it was answered privately or published - either way, the asker's own
    /// answer is now visible to them.</summary>
    public static (string Subject, string Body) ClarificationAnswered(string? locale, string referenceCode) =>
        IsEnglish(locale)
            ? ($"Your question on {referenceCode} has been answered",
               $"<p>The buyer has answered your clarification question on {referenceCode}.</p>")
            : ($"تمت الإجابة على سؤالك بخصوص {referenceCode}",
               $"<p>أجاب المشتري على سؤال الاستيضاح الخاص بك بخصوص {referenceCode}.</p>");

    /// <summary>Sent to every OTHER invited supplier when a clarification is published -
    /// deliberately does not name the asker (anonymization holds in the notification, not just the
    /// UI).</summary>
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

    /// <summary>FEAT-09.5/FR-PRP-006: the supplier's own submission receipt - "Email + in-app
    /// receipt to supplier" (BUSINESS-PROCESSES.md §4.1). Never includes pricing - a receipt
    /// confirms submission happened, it does not restate the sealed financial envelope.</summary>
    public static (string Subject, string Body) ProposalSubmitted(string? locale, string proposalReferenceCode, string rfqReferenceCode) =>
        IsEnglish(locale)
            ? ($"Proposal {proposalReferenceCode} submitted",
               $"<p>Your proposal ({proposalReferenceCode}) for {rfqReferenceCode} has been submitted successfully.</p>")
            : ($"تم إرسال العرض {proposalReferenceCode}",
               $"<p>تم إرسال عرضك ({proposalReferenceCode}) الخاص بـ {rfqReferenceCode} بنجاح.</p>");

    /// <summary>FEAT-14.4/FR-AWD-004: the winning supplier's award notice.</summary>
    public static (string Subject, string Body) AwardIssued(string? locale, string rfqReferenceCode) =>
        IsEnglish(locale)
            ? ($"You have been awarded {rfqReferenceCode}",
               $"<p>Congratulations - your proposal for {rfqReferenceCode} has been awarded. Please log in for details.</p>")
            : ($"تمت ترسية {rfqReferenceCode} عليكم",
               $"<p>تهانينا - تمت ترسية طلب عرض السعر {rfqReferenceCode} على عرضكم. يرجى تسجيل الدخول للاطلاع على التفاصيل.</p>");

    /// <summary>BRULE-082: the losing supplier's regret notice - never names the winner or states
    /// any commercial figure.</summary>
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
