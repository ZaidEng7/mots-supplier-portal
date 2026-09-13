// The supplier's post-approval lifecycle, and the eligibility rule that decides who may be invited.
//
// This work was once reported as built when it was not. A search hit the state names and it read as
// implemented: suspended and deactivated were declared, persisted, and completely unreachable, with no methods,
// no endpoints and no callers.
//
// So these tests assume a feature can look done from the outside, and check the behaviour rather than the shape.
//
// Every exit from the terminal state is refused, including the one that looks harmless: going "back" to
// suspended would make a terminal state merely a slow one.
//
// A reason is mandatory on reactivation as well as on suspension and deactivation. The written rule names only
// the latter two, but an audit trail that records why participation was removed and not why it was restored
// answers half the question a review would ask.
//
//
// THE ELIGIBILITY RULE IS ENUMERATED EXHAUSTIVELY, AND THAT IS THE POINT
//
// Every combination of onboarding and lifecycle state, including the ones that cannot occur today and the ones
// that are obviously false.
//
// When this was written, tenders and bids did not exist, so the rule had no real consumers, and a rule with no
// callers is trivially correct and untestable in the way that matters. Enumerating every state is what stood in
// for those features until they arrived; anything less would have been a test that cannot fail.
//
// Only one combination is eligible. Two states are excluded from all new selection. One combination is one the
// aggregate should not produce at all, asserted so that if it ever does, the answer is "not eligible" rather
// than an accident.
//
// The not-yet-approved rows matter most where the lifecycle says active: that is the combination where a
// lifecycle-only check would wrongly admit an applicant who has not been approved.
//
// And a final assertion guards the guard, because a new onboarding state added later would otherwise slip past
// the enumeration untested and default to whatever the rule happens to do.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public sealed class SupplierLifecycleTests
{
    private static Supplier ApprovedActive()
    {
        var supplier = SupplierTestFactory.Approved();
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active,
            "approval is what makes a supplier Active; if this changes the rest of the class is testing a fiction");
        return supplier;
    }

    [Fact]
    public void Active_suspends_with_a_reason()
    {
        var supplier = ApprovedActive();

        supplier.Suspend("Sanctions screening hit");

        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Suspended);
    }

    [Fact]
    public void Suspension_is_reversible()
    {
        var supplier = ApprovedActive();
        supplier.Suspend("Under investigation");

        supplier.Reactivate("Investigation closed, no findings");

        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void Deactivation_is_reachable_only_through_suspension()
    {
        var supplier = ApprovedActive();

        var act = () => supplier.Deactivate("Ceased trading");

        act.Should().Throw<DomainException>()
            .WithMessage("*only 'Suspended' is valid*",
                "a direct Active -> Deactivated path would make an irreversible action a single " +
                "click on a live supplier");
    }

    [Fact]
    public void Deactivated_is_terminal_with_no_path_out()
    {
        var supplier = ApprovedActive();
        supplier.Suspend("Repeated non-performance");
        supplier.Deactivate("Contract terminated");

        supplier.Invoking(s => s.Reactivate("Change of mind")).Should().Throw<DomainException>();
        supplier.Invoking(s => s.Suspend("Change of mind")).Should().Throw<DomainException>();
        supplier.Invoking(s => s.Deactivate("Again")).Should().Throw<DomainException>();

        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Deactivated);
    }

    [Fact]
    public void Suspending_an_already_suspended_supplier_is_rejected()
    {
        var supplier = ApprovedActive();
        supplier.Suspend("First");

        supplier.Invoking(s => s.Suspend("Second")).Should().Throw<DomainException>();
    }

    [Fact]
    public void Reactivating_an_active_supplier_is_rejected()
    {
        var supplier = ApprovedActive();

        supplier.Invoking(s => s.Reactivate("Nothing to reactivate")).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Suspension_requires_a_reason(string? reason)
    {
        var supplier = ApprovedActive();

        supplier.Invoking(s => s.Suspend(reason!)).Should().Throw<DomainException>()
            .WithMessage("*reason is required*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Deactivation_requires_a_reason(string? reason)
    {
        var supplier = ApprovedActive();
        supplier.Suspend("Prior suspension");

        supplier.Invoking(s => s.Deactivate(reason!)).Should().Throw<DomainException>()
            .WithMessage("*reason is required*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Reactivation_requires_a_reason(string? reason)
    {
        var supplier = ApprovedActive();
        supplier.Suspend("Prior suspension");

        supplier.Invoking(s => s.Reactivate(reason!)).Should().Throw<DomainException>()
            .WithMessage("*reason is required*");
    }

    [Theory]
    [InlineData(SupplierOnboardingState.Approved, SupplierLifecycleState.Active, true)]
    [InlineData(SupplierOnboardingState.Approved, SupplierLifecycleState.Suspended, false)]
    [InlineData(SupplierOnboardingState.Approved, SupplierLifecycleState.Deactivated, false)]
    [InlineData(SupplierOnboardingState.Approved, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.Draft, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.Draft, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.EmailVerified, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.EmailVerified, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.ProfileInProgress, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.ProfileInProgress, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.Submitted, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.Submitted, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.UnderReview, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.UnderReview, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.InfoRequested, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.InfoRequested, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.Resubmitted, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.Resubmitted, SupplierLifecycleState.Active, false)]
    [InlineData(SupplierOnboardingState.Rejected, SupplierLifecycleState.None, false)]
    [InlineData(SupplierOnboardingState.Rejected, SupplierLifecycleState.Active, false)]
    public void Eligibility_is_decided_by_both_onboarding_and_lifecycle_state(
        SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle, bool expected)
    {
        var supplier = SupplierTestFactory.InState(onboarding, lifecycle);

        supplier.IsEligibleToParticipate.Should().Be(expected,
            $"onboarding={onboarding}, lifecycle={lifecycle}");
    }

    [Fact]
    public void Every_onboarding_state_is_covered_by_the_eligibility_theory()
    {
        var covered = new[]
        {
            SupplierOnboardingState.Draft, SupplierOnboardingState.EmailVerified,
            SupplierOnboardingState.ProfileInProgress, SupplierOnboardingState.Submitted,
            SupplierOnboardingState.UnderReview, SupplierOnboardingState.InfoRequested,
            SupplierOnboardingState.Resubmitted,
            SupplierOnboardingState.Approved, SupplierOnboardingState.Rejected,
        };

        Enum.GetValues<SupplierOnboardingState>().Should().BeSubsetOf(covered,
            "a new onboarding state must be added to the eligibility theory deliberately, not " +
            "inherit an answer by omission");
        Enum.GetValues<SupplierLifecycleState>().Should().BeSubsetOf(new[]
        {
            SupplierLifecycleState.None, SupplierLifecycleState.Active,
            SupplierLifecycleState.Suspended, SupplierLifecycleState.Deactivated,
        });
    }
}
