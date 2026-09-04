using FluentAssertions;
using MotsSupplierPortal.Application.Suppliers;

namespace MotsSupplierPortal.Tests.Unit;

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
        // The control for the case above. A ratio that always returned 1 would pass it.
        ProfileCompleteness.Ratio(missingItems: 10, totalItems: 10).Should().Be(0);
    }

    [Fact]
    public void The_fraction_is_satisfied_over_total_rounded_to_two_places()
    {
        // §12.2's own example is 0.62, so two places is the documented precision.
        ProfileCompleteness.Ratio(missingItems: 5, totalItems: 13).Should().Be(0.62);
    }

    [Fact]
    public void No_requirements_reads_as_complete_rather_than_as_nothing_done()
    {
        // A tenant with no required document types and no checklist is not a supplier who has
        // failed to do anything - and 0 would render as an empty bar with no explanation.
        ProfileCompleteness.Ratio(missingItems: 0, totalItems: 0).Should().Be(1);
    }

    [Fact]
    public void More_missing_than_total_still_clamps_to_zero_rather_than_going_negative()
    {
        // Defensive: the two counts come from separate queries, and a negative meter would render
        // as an inverted bar rather than as an error anybody notices.
        ProfileCompleteness.Ratio(missingItems: 12, totalItems: 10).Should().Be(0);
    }
}
