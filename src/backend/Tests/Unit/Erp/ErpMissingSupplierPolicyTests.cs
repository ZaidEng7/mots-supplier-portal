// Whether a run may suspend the suppliers that have vanished from the ERP.
//
// THE EMPTY LIST IS THE TEST THAT MATTERS MOST. A narrowed credential or a server answering politely with nothing
// returns zero suppliers, and a job that believed it would suspend every supplier the ministry has overnight. It is
// asserted with a portal full of suppliers, because that is the situation in which believing it does the damage.
//
// THE FLOOR IS TESTED AT BOTH EDGES. With a handful of suppliers a quarter rounds to one, and without the floor a
// single genuine deletion would be refused on every run forever; the test shows five is allowed and six is not when
// a quarter would be smaller.
//
// THE CONTROL IS NOTHING MISSING, which must always be allowed - including against an empty ERP and an empty portal,
// the state of every fresh deployment. A policy that refused there would put a warning on the first run anyone sees.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using FluentAssertions;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpMissingSupplierPolicyTests
{
    [Theory]
    [InlineData(80, 80)]
    [InlineData(0, 0)]
    public void With_nothing_missing_there_is_nothing_to_hold_back(int inErp, int linked)
    {
        ErpMissingSupplierPolicy.Decide(inErp, linked, missingFromErp: 0).MaySuspend.Should().BeTrue();
    }

    [Fact]
    public void An_empty_list_from_the_erp_is_never_believed()
    {
        var decision = ErpMissingSupplierPolicy.Decide(suppliersInErp: 0, linkedInPortal: 80, missingFromErp: 80);

        decision.MaySuspend.Should().BeFalse(
            "an ERP does not lose every supplier overnight - this is a broken read, and believing it would suspend "
            + "the ministry's whole supplier base");
        decision.HeldBackBecause.Should().Contain("returned no suppliers");
    }

    [Fact]
    public void A_few_real_deletions_are_allowed()
    {
        ErpMissingSupplierPolicy.Decide(suppliersInErp: 77, linkedInPortal: 80, missingFromErp: 3)
            .MaySuspend.Should().BeTrue();
    }

    [Fact]
    public void A_cliff_is_held_back_and_the_message_says_what_and_why()
    {
        var decision = ErpMissingSupplierPolicy.Decide(suppliersInErp: 40, linkedInPortal: 80, missingFromErp: 40);

        decision.MaySuspend.Should().BeFalse("half the suppliers vanishing in one night is a fault, not a clear-out");
        decision.HeldBackBecause.Should().Contain("40 suppliers").And.Contain("20");
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void With_few_suppliers_the_floor_of_five_applies(int missing, bool allowed)
    {
        ErpMissingSupplierPolicy.Decide(suppliersInErp: 10 - missing, linkedInPortal: 10, missingFromErp: missing)
            .MaySuspend.Should().Be(
                allowed,
                "a quarter of ten rounds to two, and without the floor one genuine deletion could be refused forever");
    }

    [Fact]
    public void Exactly_a_quarter_is_allowed_and_one_more_is_not()
    {
        ErpMissingSupplierPolicy.Decide(60, 80, 20).MaySuspend.Should().BeTrue();
        ErpMissingSupplierPolicy.Decide(59, 80, 21).MaySuspend.Should().BeFalse();
    }
}
