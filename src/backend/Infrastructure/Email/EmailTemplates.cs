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
