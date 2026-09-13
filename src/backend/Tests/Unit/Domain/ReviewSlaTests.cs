// The review target, counted in working days.
//
// The written process runs a timer and names no number, so the duration is configurable and the date it produces
// is a TARGET rather than a breach.
//
// Friday and Saturday are the weekend, which is the Syrian working week and the only assumption the calculation
// makes.
//
//
// WHY IT COUNTS WORKING DAYS AT ALL
//
// Thursday plus one working day is SUNDAY, not Friday. A calendar-day count would have made a Thursday arrival
// due on the weekend.
//
// Every duration the setting permits is asserted from every day of one week, because the bound the word "target"
// depends on is that the date it names is a day somebody is working.
//
// The time of day is not shifted. A target is a date, and moving the clock as well would make two cases submitted
// an hour apart land on different notional deadlines for no reason anybody stated.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Configuration;
using Xunit;

public sealed class ReviewSlaTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Five_working_days_from_a_monday_lands_on_the_following_monday()
    {
        var target = ReviewSla.TargetFor(Monday, 5);

        target.DayOfWeek.Should().Be(DayOfWeek.Monday);
        target.Date.Should().Be(new DateTime(2026, 9, 14));
    }

    [Fact]
    public void A_case_arriving_on_a_thursday_is_not_late_before_anyone_is_at_their_desk()
    {
        var thursday = new DateTimeOffset(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);
        thursday.DayOfWeek.Should().Be(DayOfWeek.Thursday, "the fixture's own premise");

        var target = ReviewSla.TargetFor(thursday, 1);

        target.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void The_target_never_falls_on_a_weekend()
    {
        for (var offset = 0; offset < 7; offset++)
        {
            for (var days = 1; days <= 60; days++)
            {
                var target = ReviewSla.TargetFor(Monday.AddDays(offset), days);
                target.DayOfWeek.Should().NotBe(DayOfWeek.Friday);
                target.DayOfWeek.Should().NotBe(DayOfWeek.Saturday);
            }
        }
    }

    [Fact]
    public void The_time_of_day_is_carried_through_unchanged()
    {
        var target = ReviewSla.TargetFor(Monday, 3);

        target.TimeOfDay.Should().Be(Monday.TimeOfDay);
    }
}
