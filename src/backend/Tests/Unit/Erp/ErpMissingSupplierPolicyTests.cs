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
// SUPPLIERS ALREADY OUT OF SERVICE ARE ONLY MARKED, and only a read that is not believed stops that: an empty one,
// or one over the quarter for ACTIVE suppliers. The quarter is never applied to the count of marks, on purpose:
// suspending held-back suppliers here is how a person confirms a real clear-out, and a limit on the marks would hold
// that confirmation back every night. Both halves are asserted, here and in ErpSyncPlanTests.
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
        var decision = ErpMissingSupplierPolicy.Decide(suppliersInErp: 0, activeLinkedInPortal: 80, missingFromErp: 80);

        decision.MaySuspend.Should().BeFalse(
            "an ERP does not lose every supplier overnight - this is a broken read, and believing it would suspend "
            + "the ministry's whole supplier base");
        decision.HeldBackBecause.Should().Contain("returned no suppliers");
    }

    [Fact]
    public void An_empty_list_is_not_believed_even_when_only_suppliers_already_out_of_service_are_missing()
    {
        var decision = ErpMissingSupplierPolicy.Decide(
            suppliersInErp: 0, activeLinkedInPortal: 0, missingFromErp: 0, outOfServiceMissingFromErp: 12);

        decision.MaySuspend.Should().BeFalse("an empty read would otherwise mark every suspended supplier as gone");
        decision.HeldBackBecause.Should().Contain("returned no suppliers").And.Contain("12");
    }

    [Fact]
    public void Suppliers_already_out_of_service_are_not_held_to_the_quarter()
    {
        ErpMissingSupplierPolicy.Decide(
                suppliersInErp: 20, activeLinkedInPortal: 20, missingFromErp: 0, outOfServiceMissingFromErp: 30)
            .MaySuspend.Should().BeTrue(
                "a person confirms a held-back clear-out by suspending those suppliers here; holding their marks too "
                + "would leave nothing that could clear it");
    }

    [Fact]
    public void A_few_real_deletions_are_allowed()
    {
        ErpMissingSupplierPolicy.Decide(suppliersInErp: 77, activeLinkedInPortal: 80, missingFromErp: 3)
            .MaySuspend.Should().BeTrue();
    }

    [Fact]
    public void A_cliff_is_held_back_and_the_message_says_what_and_why()
    {
        var decision = ErpMissingSupplierPolicy.Decide(suppliersInErp: 40, activeLinkedInPortal: 80, missingFromErp: 40);

        decision.MaySuspend.Should().BeFalse("half the suppliers vanishing in one night is a fault, not a clear-out");
        decision.HeldBackBecause.Should().Contain("40 suppliers").And.Contain("20");
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void With_few_suppliers_the_floor_of_five_applies(int missing, bool allowed)
    {
        ErpMissingSupplierPolicy.Decide(suppliersInErp: 10 - missing, activeLinkedInPortal: 10, missingFromErp: missing)
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
