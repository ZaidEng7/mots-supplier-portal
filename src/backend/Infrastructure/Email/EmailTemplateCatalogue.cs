// The shipped wording of every email, as a TEMPLATE with its tokens still in it.
//
//
// RECOVERED FROM THE REAL COPY RATHER THAN TRANSCRIBED FROM IT
//
// Each entry calls the real method with the token NAMES as its arguments, so the language's own interpolation
// hands back the shipped sentence with the token sitting where the value goes.
//
// That keeps one copy of nineteen emails in two languages. A second transcription would have been the thing that
// drifts, and it would drift silently, because nothing renders the copy in this file.
//
// It also makes the token contract checkable against reality: a required token the shipped copy does not actually
// contain is a contract nobody could satisfy, and a test asserts exactly that.
//
// The shipped rows report the epoch as their timestamp rather than the current moment, because they are not an
// edit, and a timestamp that moved on every read would make the screen look as though somebody had just changed
// the shipped wording.
//
//
// A TOKEN WITH NO VALUE IS LEFT ALONE
//
// Rather than replaced with nothing. That is the safer failure: a visible token arriving in an email is a bug
// somebody reports within the hour, where a sentence that silently loses its link reads perfectly and strands the
// recipient.

namespace MotsSupplierPortal.Infrastructure.Email;

using MotsSupplierPortal.Application.Admin;

public static class EmailTemplateCatalogue
{
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

    public static EmailTemplateOverrideDto ShippedFor(string key)
    {
        var render = Shipped[key];
        var (subjectAr, bodyAr) = render("ar");
        var (subjectEn, bodyEn) = render("en");
        return new EmailTemplateOverrideDto(key, subjectAr, subjectEn, bodyAr, bodyEn, DateTimeOffset.UnixEpoch);
    }

    public static bool Knows(string key) => Shipped.ContainsKey(key);

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
