// The rule that decides when a security event is spiking on the administrator's dashboard, in isolation from the
// database: a floor of five in the last 24 hours, at least three times the daily average of the days before, and
// that average taken only over the days whose rows were being stored. The second figure is the event's rows over
// those stored days before the last 24 hours, which the section counts from BaselineStart.
//
// The numbers are held as literals rather than read from the rule's constants, so a change to the rule is a change
// somebody has to make here too.

namespace MotsSupplierPortal.Tests.Unit.Admin;

using FluentAssertions;
using MotsSupplierPortal.Application.Admin.Dashboard;

public sealed class DashboardSecuritySpikeTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LongAgo = AsOf.AddDays(-400);

    [Fact]
    public void Below_five_in_a_day_is_never_a_spike_however_quiet_the_week()
    {
        DashboardSecuritySpike.IsSpiking(4, 0, LongAgo, AsOf).Should().BeFalse();
        DashboardSecuritySpike.IsSpiking(5, 0, LongAgo, AsOf).Should().BeTrue();
    }

    [Fact]
    public void A_day_is_a_spike_at_three_times_the_daily_average_of_the_six_days_before()
    {
        // Twelve over the six days before is an average of two a day, so six is three times it and five is not.
        DashboardSecuritySpike.IsSpiking(6, 12, LongAgo, AsOf).Should().BeTrue();
        DashboardSecuritySpike.IsSpiking(5, 12, LongAgo, AsOf).Should().BeFalse();
    }

    [Fact]
    public void A_busy_week_makes_a_busy_day_ordinary()
    {
        DashboardSecuritySpike.IsSpiking(40, 6 * 40, LongAgo, AsOf).Should().BeFalse();
    }

    [Fact]
    public void The_average_counts_only_the_days_that_were_stored()
    {
        // Stored for three days before the last 24 hours, with six rows in them: two a day, not one.
        var countedSince = AsOf.AddDays(-4);

        DashboardSecuritySpike.IsSpiking(5, 6, countedSince, AsOf).Should().BeFalse(
            "five is not three times two");
        DashboardSecuritySpike.IsSpiking(6, 6, countedSince, AsOf).Should().BeTrue();
        DashboardSecuritySpike.IsSpiking(5, 6, LongAgo, AsOf).Should().BeTrue(
            "over six stored days the same six rows are one a day");
    }

    [Fact]
    public void With_less_than_a_whole_day_stored_before_the_last_24_hours_nothing_is_a_spike()
    {
        DashboardSecuritySpike.IsSpiking(50, 0, AsOf.AddHours(-47), AsOf).Should().BeFalse();
        DashboardSecuritySpike.IsSpiking(50, 0, AsOf.AddHours(-3), AsOf).Should().BeFalse();
        DashboardSecuritySpike.IsSpiking(50, 0, AsOf.AddHours(-48), AsOf).Should().BeTrue();
    }

    [Fact]
    public void Nothing_stored_at_all_is_never_a_spike()
    {
        DashboardSecuritySpike.IsSpiking(50, 0, null, AsOf).Should().BeFalse();
    }

    [Fact]
    public void The_baseline_starts_at_the_later_of_the_day_counted_from_and_a_week_back()
    {
        DashboardSecuritySpike.BaselineStart(AsOf.AddDays(-4), AsOf).Should().Be(AsOf.AddDays(-4));
        DashboardSecuritySpike.BaselineStart(LongAgo, AsOf).Should().Be(AsOf.AddDays(-7));
        DashboardSecuritySpike.BaselineStart(null, AsOf).Should().Be(AsOf.AddDays(-7));
    }
}
