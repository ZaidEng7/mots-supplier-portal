using System.Globalization;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Governance;

namespace MotsSupplierPortal.Tests.Unit.Governance;

/// <summary>
/// The Ministry's spend chart buckets awards by month, and the month has to be the one the rest of the
/// screen is written in.
///
/// <para><b>The defect.</b> The bucket key was <c>ToString("yyyy-MM")</c> with no culture. That formats
/// in the current culture's CALENDAR, and this application serves Arabic, so on a machine or request
/// resolving to an Arabic culture the axis read "1448-01" and "1448-02" - Hijri years, beside a table of
/// Gregorian dates. It shipped because it is unreachable without data: with no awards there are no
/// buckets, and the demonstration database had none until awards were seeded.</para>
///
/// <para>Asserted under a culture that actually changes the answer, rather than under the invariant one
/// where every implementation passes.</para>
/// </summary>
public sealed class MinistryAwardMonthTests
{
    private static readonly DateTimeOffset July2026 = new(2026, 7, 14, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-GB")]
    [InlineData("ar-SA")]     // Umm al-Qura calendar - the one that produced "1448-07"
    [InlineData("fa-IR")]     // Persian calendar, a second non-Gregorian default
    [InlineData("th-TH")]     // Buddhist calendar, which shifts the YEAR rather than the month
    public void The_bucket_is_the_gregorian_month_whatever_culture_is_current(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            MinistryAwardMonth.Of(July2026).Should().Be("2026-07");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_culture_under_test_really_would_have_changed_the_answer()
    {
        // The control. Without this the theory above would pass just as well against the original
        // implementation on a machine whose cultures all happen to be Gregorian, which is how a guard
        // ends up asserting nothing on the only machines that run it.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
        try
        {
            July2026.ToString("yyyy-MM").Should().NotBe("2026-07");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
