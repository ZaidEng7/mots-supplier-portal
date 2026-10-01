// Reading the ERP's timestamps, which arrive with no time zone on them.
//
// THE CONTROL IS THE THIRD ASSERTION IN THE FIRST TEST, and it is the reason this file exists: the same string
// read as UTC is three hours away from the right answer. That is what the framework's own parse does by default
// on a container whose clock is UTC, and it produces a value that saves, renders and exports without one
// complaint - so a test that only checked "does it parse" would pass on the broken reading.
//
// A VALUE THAT ALREADY CARRIES A ZONE MUST SURVIVE UNTOUCHED. Some of the ERP's endpoints do emit an offset, and
// re-interpreting an explicit Z as if it were Damascus local time corrupts the one case that arrived correct.
// That is the opposite mistake and it needs its own test, because a fix for one direction is exactly how the
// other direction breaks.
//
// THE MICROSECONDS ARE NOT HYPOTHETICAL. Every creation timestamp on the test server carries six decimal places
// - "2026-09-16 12:33:48.421891" - and the documented wire format has none. A reader written to the documentation
// alone rejects every row the real server sends.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpServerTimeTests
{
    private static readonly TimeZoneInfo Damascus = ErpServerTime.Zone(ErpOptions.DefaultTimeZone);

    [Fact]
    public void A_timestamp_with_no_zone_is_read_as_the_erp_servers_local_time()
    {
        var parsed = ErpServerTime.Parse("2026-08-17 03:15:00", Damascus);

        parsed.Offset.Should().Be(TimeSpan.FromHours(3), "the ERP's clock is Damascus time, not the reader's");
        parsed.UtcDateTime.Should().Be(new DateTime(2026, 8, 17, 0, 15, 0, DateTimeKind.Utc));

        parsed.UtcDateTime.Should().NotBe(
            new DateTime(2026, 8, 17, 3, 15, 0, DateTimeKind.Utc),
            "reading it as UTC is what a default parse does on a UTC container, and it is wrong by three hours "
            + "in a way that saves, renders and exports without one complaint");
    }

    [Fact]
    public void A_timestamp_that_already_carries_a_zone_is_honoured_rather_than_reinterpreted()
    {
        var parsed = ErpServerTime.Parse("2026-08-17T03:15:00Z", Damascus);

        parsed.UtcDateTime.Should().Be(
            new DateTime(2026, 8, 17, 3, 15, 0, DateTimeKind.Utc),
            "some of the ERP's endpoints do send an offset, and shifting one that arrived correct is the "
            + "opposite mistake");
    }

    [Fact]
    public void The_microseconds_the_real_server_sends_are_accepted()
    {
        var parsed = ErpServerTime.Parse("2026-09-16 12:33:48.421891", Damascus);

        parsed.Offset.Should().Be(TimeSpan.FromHours(3));
        parsed.UtcDateTime.Should().Be(new DateTime(2026, 9, 16, 9, 33, 48, DateTimeKind.Utc).AddTicks(4218910),
            "every creation timestamp on the test server carries six decimal places the documented format omits");
    }

    [Fact]
    public void A_date_with_no_time_is_accepted()
    {
        ErpServerTime.TryParse("2026-08-17", Damascus, out var parsed).Should().BeTrue();
        parsed.Offset.Should().Be(TimeSpan.FromHours(3));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-timestamp")]
    [InlineData("17/08/2026 03:15:00")]
    public void Anything_that_is_not_a_timestamp_the_erp_sends_is_refused(string? value)
    {
        ErpServerTime.TryParse(value, Damascus, out _).Should().BeFalse(
            $"'{value}' is not a value this should invent a time from");
    }

    [Fact]
    public void An_unknown_zone_identifier_throws_rather_than_falling_back_to_utc()
    {
        var act = () => ErpServerTime.Zone("Mars/Olympus_Mons");

        act.Should().Throw<TimeZoneNotFoundException>(
            "a silent fallback would turn one configuration mistake into permanently skewed timestamps");
    }

    [Fact]
    public void A_time_sent_to_the_erp_is_written_on_its_clock_with_no_offset()
    {
        ErpServerTime.Format(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), Damascus)
            .Should().Be("2026-09-30 12:00:00", "the ERP compares it with creation times it stores in Damascus time");
    }

    [Fact]
    public void A_formatted_time_reads_back_as_the_same_moment()
    {
        var moment = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

        ErpServerTime.Parse(ErpServerTime.Format(moment, Damascus), Damascus).Should().Be(moment);
    }
}
