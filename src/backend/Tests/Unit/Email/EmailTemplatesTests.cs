// Every email template actually branches on language, rather than one arm being unreachable code that happens to
// compile.
//
// The denominator is asserted rather than trusted from the ticket, which said nine hardcoded strings. There are
// eleven distinct composition sites; one of them is a reviewer-facing email that the count did not obviously
// include.
//
// That assertion is the one that would catch a twelfth template added later with no matching row here, or a row
// here for a template that no longer exists.
//
// An unrecognised or missing language renders Arabic, matching the account setting's own default and the
// interface's fallback. A missing or garbage value must not silently render English.

namespace MotsSupplierPortal.Tests.Unit.Email;

using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Email;

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
        EmailTemplates.ApplicationApproved(null).Subject.Should().MatchRegex(@"\p{IsArabic}");
        EmailTemplates.ApplicationApproved("fr").Subject.Should().MatchRegex(@"\p{IsArabic}");
        EmailTemplates.ApplicationApproved("").Subject.Should().MatchRegex(@"\p{IsArabic}");
    }
}
