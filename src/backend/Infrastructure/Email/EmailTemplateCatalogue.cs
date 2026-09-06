using MotsSupplierPortal.Application.Admin;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// T-076. The shipped wording of every email, as a TEMPLATE with its tokens still in it.
///
/// <para><b>Recovered from EmailTemplates rather than copied out of it.</b> Each entry calls the real
/// method with the token NAMES as its arguments - <c>Verification(locale, "{verifyUrl}")</c> - so C#'s own
/// interpolation hands back the shipped sentence with <c>{verifyUrl}</c> sitting where the link goes. That
/// keeps one copy of nineteen emails in two languages: a second transcription would have been the thing
/// that drifts, and it would drift silently, because nothing renders the copy in this file.</para>
///
/// <para>It also means the token contract in <see cref="EmailTemplateKeys"/> is checkable against reality:
/// a required token that the shipped copy does not actually contain is a contract nobody could satisfy, and
/// EmailTemplateCatalogueTests asserts exactly that.</para>
/// </summary>
public static class EmailTemplateCatalogue
{
    /// <summary>A token as it appears in a template body. One spelling, used by the catalogue, the
    /// validator and the renderer.</summary>
    public static string Placeholder(string token) => $"{{{token}}}";

    private static readonly Dictionary<string, Func<string?, (string Subject, string Body)>> Shipped =
        new(StringComparer.Ordinal)
        {
            [EmailTemplateKeys.Verification] = l => EmailTemplates.Verification(l, Placeholder("verifyUrl")),
            [EmailTemplateKeys.PasswordReset] = l => EmailTemplates.PasswordReset(l, Placeholder("resetUrl")),
            [EmailTemplateKeys.SupplierUserInvite] = l => EmailTemplates.SupplierUserInvite(l, Placeholder("acceptUrl")),
            [EmailTemplateKeys.StaffInvite] = l => EmailTemplates.StaffInvite(l, Placeholder("acceptUrl")),
            [EmailTemplateKeys.AlreadyRegisteredNotice] = l => EmailTemplates.AlreadyRegisteredNotice(l, Placeholder("publicUrl")),
            [EmailTemplateKeys.ApplicationApproved] = EmailTemplates.ApplicationApproved,
            [EmailTemplateKeys.ApplicationRejected] = l => EmailTemplates.ApplicationRejected(l, Placeholder("reason")),
            [EmailTemplateKeys.InfoRequested] = l => EmailTemplates.InfoRequested(l, Placeholder("reason")),
            [EmailTemplateKeys.ApplicationResubmitted] = l => EmailTemplates.ApplicationResubmitted(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.RfqInvitation] = l => EmailTemplates.RfqInvitation(
                l, Placeholder("referenceCode"), Placeholder("rfqTitle"), Placeholder("deepLink")),
            [EmailTemplateKeys.ClarificationAnswered] = l => EmailTemplates.ClarificationAnswered(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.ClarificationPublished] = l => EmailTemplates.ClarificationPublished(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.ClarificationPosted] = l => EmailTemplates.ClarificationPosted(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.RfqAddendum] = l => EmailTemplates.RfqAddendum(l, Placeholder("referenceCode"), Placeholder("addendumTitle")),
            [EmailTemplateKeys.RfqPublished] = l => EmailTemplates.RfqPublished(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.RfqCancelled] = l => EmailTemplates.RfqCancelled(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.ProposalSubmitted] = l => EmailTemplates.ProposalSubmitted(
                l, Placeholder("proposalReferenceCode"), Placeholder("rfqReferenceCode")),
            [EmailTemplateKeys.EvaluatorAssigned] = l => EmailTemplates.EvaluatorAssigned(l, Placeholder("referenceCode")),
            [EmailTemplateKeys.AwardIssued] = l => EmailTemplates.AwardIssued(l, Placeholder("rfqReferenceCode")),
            [EmailTemplateKeys.AwardRegret] = l => EmailTemplates.AwardRegret(l, Placeholder("rfqReferenceCode")),
            [EmailTemplateKeys.DocumentRejected] = l => EmailTemplates.DocumentRejected(l, Placeholder("fileName"), Placeholder("reason")),
            [EmailTemplateKeys.DocumentExpiring] = l => EmailTemplates.DocumentExpiring(l, Placeholder("fileName")),
            [EmailTemplateKeys.DocumentExpired] = l => EmailTemplates.DocumentExpired(l, Placeholder("fileName")),
        };

    /// <summary>The shipped copy for one key in both locales, tokens intact.</summary>
    public static EmailTemplateOverrideDto ShippedFor(string key)
    {
        var render = Shipped[key];
        var (subjectAr, bodyAr) = render("ar");
        var (subjectEn, bodyEn) = render("en");
        // UpdatedAt is the epoch rather than "now": this row is not an edit, and a timestamp that moved on
        // every read would make the screen look as though someone had just changed the shipped wording.
        return new EmailTemplateOverrideDto(key, subjectAr, subjectEn, bodyAr, bodyEn, DateTimeOffset.UnixEpoch);
    }

    public static bool Knows(string key) => Shipped.ContainsKey(key);

    /// <summary>
    /// Substitutes the caller's values into a template body or subject.
    ///
    /// <para>A token with no value is left ALONE rather than replaced with an empty string. That is the
    /// safer failure: <c>{deepLink}</c> arriving visibly in an email is a bug someone reports in an hour,
    /// where a sentence that silently loses its link reads perfectly and strands the recipient.</para>
    /// </summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var rendered = template;
        foreach (var (token, value) in tokens)
        {
            rendered = rendered.Replace(Placeholder(token), value, StringComparison.Ordinal);
        }
        return rendered;
    }
}
