using FluentAssertions;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Notifications;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// The copy catalogue must cover the notification types exactly - in BOTH directions.
///
/// <para>A type with no copy would reach a supplier as an empty row; copy for a type nothing emits
/// is approved wording for an event that no longer happens, which a product owner keeps re-reading.
/// The same shape as §7.2's validation catalogue, for the same reasons.</para>
/// </summary>
public sealed class NotificationCatalogueCoverageTests
{
    [Fact]
    public void Every_notification_type_has_authored_copy()
    {
        var missing = NotificationTypes.All.Except(NotificationCatalogue.Types).Order().ToList();

        missing.Should().BeEmpty(
            "a type with no entry in NotificationCatalogue.jsonc has no words to show");
    }

    [Fact]
    public void Every_catalogue_entry_matches_a_notification_type()
    {
        var orphaned = NotificationCatalogue.Types.Except(NotificationTypes.All).Order().ToList();

        orphaned.Should().BeEmpty(
            "copy for a type nothing emits is reviewed and approved wording for a dead event");
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_entry_carries_both_languages_in_both_fields(string type)
    {
        var entry = NotificationCatalogue.For(type);

        entry.TitleAr.Should().NotBeNullOrWhiteSpace();
        entry.TitleEn.Should().NotBeNullOrWhiteSpace();
        entry.BodyAr.Should().NotBeNullOrWhiteSpace();
        entry.BodyEn.Should().NotBeNullOrWhiteSpace();

        // The Arabic must be Arabic script, not the English copied into both slots - which is what a
        // half-finished entry looks like, and what a reviewer would miss in a file of nineteen.
        entry.TitleAr.Should().MatchRegex("[؀-ۿ]");
        entry.BodyAr.Should().MatchRegex("[؀-ۿ]");
        entry.TitleAr.Should().NotBe(entry.TitleEn);
    }

    public static TheoryData<string> AllTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in NotificationTypes.All.Order()) data.Add(type);
        return data;
    }
}
