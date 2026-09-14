// The completeness fraction a supplier's meter shows.
//
// Each case has the control it needs: a ratio that always returned a full meter would pass the first assertion.
//
// Two decimal places, because the written contract's own example carries that precision.
//
// A supplier with nothing required of them reads as complete rather than as zero. No requirements is a finished
// checklist, and a zero would render as an empty bar with no explanation.
//
// And a negative can never be produced, which is defensive rather than reachable: the two counts come from
// separate queries, and a negative meter would render as an inverted bar rather than as an error anybody notices.

namespace MotsSupplierPortal.Tests.Unit.Suppliers;

using FluentAssertions;
using MotsSupplierPortal.Application.Suppliers;

public sealed class ProfileCompletenessTests
{
    [Fact]
    public void A_supplier_who_has_done_everything_is_one()
    {
        ProfileCompleteness.Ratio(missingItems: 0, totalItems: 10).Should().Be(1);
    }

    [Fact]
    public void A_supplier_who_has_done_nothing_is_zero()
    {
        ProfileCompleteness.Ratio(missingItems: 10, totalItems: 10).Should().Be(0);
    }

    [Fact]
    public void The_fraction_is_satisfied_over_total_rounded_to_two_places()
    {
        ProfileCompleteness.Ratio(missingItems: 5, totalItems: 13).Should().Be(0.62);
    }

    [Fact]
    public void No_requirements_reads_as_complete_rather_than_as_nothing_done()
    {
        ProfileCompleteness.Ratio(missingItems: 0, totalItems: 0).Should().Be(1);
    }

    [Fact]
    public void More_missing_than_total_still_clamps_to_zero_rather_than_going_negative()
    {
        ProfileCompleteness.Ratio(missingItems: 12, totalItems: 10).Should().Be(0);
    }
}
