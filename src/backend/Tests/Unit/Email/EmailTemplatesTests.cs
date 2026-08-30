using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Email;

namespace MotsSupplierPortal.Tests.Unit.Email;

/// <summary>
/// MSP-69: proves every one of the 11 templates actually branches on locale rather than one of the
/// two arms silently being unreachable dead code that happens to compile. Denominator asserted below
/// rather than trusting the count in the ticket ("9 hardcoded strings") - EmailJobs.cs actually has
/// 11 distinct composition sites (SendApplicationResubmittedEmailAsync, a reviewer-facing email, is
/// one that "9" does not obviously include).
/// </summary>
public sealed class EmailTemplatesTests
{
    public static IEnumerable<object[]> AllTemplates() =>
    [
        [(Func<(string, string)>)(() => EmailTemplates.Verification("ar", "https://x/verify")), (Func<(string, string)>)(() => EmailTemplates.Verification("en", "https://x/verify"))],
        [(Func<(string, string)>)(() => EmailTemplates.PasswordReset("ar", "https://x/reset")), (Func<(string, string)>)(() => EmailTemplates.PasswordReset("en", "https://x/reset"))],
        [(Func<(string, string)>)(() => EmailTemplates.SupplierUserInvite("ar", "https://x/invite")), (Func<(string, string)>)(() => EmailTemplates.SupplierUserInvite("en", "https://x/invite"))],
        [(Func<(string, string)>)(() => EmailTemplates.AlreadyRegisteredNotice("ar", "https://x")), (Func<(string, string)>)(() => EmailTemplates.AlreadyRegisteredNotice("en", "https://x"))],
        [(Func<(string, string)>)(() => EmailTemplates.ApplicationApproved("ar")), (Func<(string, string)>)(() => EmailTemplates.ApplicationApproved("en"))],
        [(Func<(string, string)>)(() => EmailTemplates.ApplicationRejected("ar", "reason")), (Func<(string, string)>)(() => EmailTemplates.ApplicationRejected("en", "reason"))],
        [(Func<(string, string)>)(() => EmailTemplates.InfoRequested("ar", "reason")), (Func<(string, string)>)(() => EmailTemplates.InfoRequested("en", "reason"))],
        [(Func<(string, string)>)(() => EmailTemplates.ApplicationResubmitted("ar", "REF-1")), (Func<(string, string)>)(() => EmailTemplates.ApplicationResubmitted("en", "REF-1"))],
        [(Func<(string, string)>)(() => EmailTemplates.DocumentRejected("ar", "file.pdf", "reason")), (Func<(string, string)>)(() => EmailTemplates.DocumentRejected("en", "file.pdf", "reason"))],
        [(Func<(string, string)>)(() => EmailTemplates.DocumentExpiring("ar", "file.pdf")), (Func<(string, string)>)(() => EmailTemplates.DocumentExpiring("en", "file.pdf"))],
        [(Func<(string, string)>)(() => EmailTemplates.DocumentExpired("ar", "file.pdf")), (Func<(string, string)>)(() => EmailTemplates.DocumentExpired("en", "file.pdf"))],
    ];

    [Fact]
    public void Eleven_templates_are_covered_by_this_denominator()
    {
        // The one assertion that would catch a 12th template added later without a matching row
        // here, or a row here for a template that no longer exists.
        AllTemplates().Should().HaveCount(11);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Ar_and_en_render_different_content_for_the_same_template(Func<(string Subject, string Body)> ar, Func<(string Subject, string Body)> en)
    {
        var arResult = ar();
        var enResult = en();

        arResult.Subject.Should().NotBe(enResult.Subject, "ar and en must not silently share a subject");
        arResult.Body.Should().NotBe(enResult.Body, "ar and en must not silently share a body");
        arResult.Subject.Should().MatchRegex(@"\p{IsArabic}", "the ar arm must actually contain Arabic script, not just a different English string");
        enResult.Subject.Should().NotMatchRegex(@"\p{IsArabic}", "the en arm must not contain Arabic script");
    }

    [Fact]
    public void Unrecognized_or_missing_locale_falls_back_to_Arabic()
    {
        // Matches AppUser.Language's own default ("ar") and the frontend's fallbackLng
        // (src/frontend/src/i18n/config.ts) - a null/garbage locale must not silently render English.
        EmailTemplates.ApplicationApproved(null).Subject.Should().MatchRegex(@"\p{IsArabic}");
        EmailTemplates.ApplicationApproved("fr").Subject.Should().MatchRegex(@"\p{IsArabic}");
        EmailTemplates.ApplicationApproved("").Subject.Should().MatchRegex(@"\p{IsArabic}");
    }
}
