using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// Task #18/MSP-82: proves the six compliance-critical domain methods return the SAME fact
/// <c>EnsureEditableForComplianceField</c> computes internally, rather than a caller having to
/// re-derive an approximation of it later by diffing OnboardingState (which is what
/// ComplianceReTrigger did before this fix, and which was correct only because no other code path
/// could produce the same state transition for a different reason - see ComplianceReTrigger's own
/// doc comment).
///
/// <para>These are ordinary behavioural assertions, not a demonstration of live divergence - there
/// is no second code path in today's domain model that WOULD diverge from the old heuristic, which
/// is exactly the point: the old code's correctness could not be exercised into failing today, only
/// argued about. What these tests prove instead is that the return value now IS the domain's own
/// answer, not a proxy for it - reverting any of the six methods back to void/the bare value
/// (discarding EnsureEditableForComplianceField's result) makes this file fail to compile, which is
/// the strongest form of revert-to-red available for an API-shape fix like this one.</para>
/// </summary>
public sealed class ComplianceReTriggerSignalTests
{
    [Fact]
    public void UpdateLegalInfo_reports_reTriggered_true_when_compliance_critical_on_an_Approved_supplier()
    {
        var supplier = SupplierTestFactory.Approved();

        var reTriggered = supplier.UpdateLegalInfo("اسم", "Name", "REG-1", null, SupplierLegalType.Company, null, isComplianceCritical: true);

        reTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void UpdateLegalInfo_reports_reTriggered_false_when_editing_is_allowed_without_retriggering()
    {
        // Approved + not-compliance-critical isn't a case EnsureEditableForComplianceField ever
        // returns false for - it falls through to EnsureEditable(), which refuses non-critical
        // edits on an Approved supplier outright (there is nothing to retrigger FROM). The
        // false case is reachable pre-approval, while ordinary edits are still allowed.
        var supplier = Supplier.Register(
            "SUP-CRT-2", "شركة اختبار", "CRT Test Co 2", null, "Rep", "crt2@example.com");
        supplier.MarkEmailVerified();

        var reTriggered = supplier.UpdateLegalInfo("اسم", "Name", "REG-1", null, SupplierLegalType.Company, null, isComplianceCritical: false);

        reTriggered.Should().BeFalse();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress,
            "UpdateLegalInfo advances EmailVerified -> ProfileInProgress on any successful edit - not a retrigger, just normal onboarding progress");
    }

    [Fact]
    public void AddBankAccount_UpdateBankAccount_and_RemoveBankAccount_all_report_the_signal()
    {
        var supplier = SupplierTestFactory.Approved();

        var (account, addReTriggered) = supplier.AddBankAccount("Holder", "Bank", null, [1, 2, 3], "****1234", null, "SYP", isComplianceCritical: true);
        addReTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);

        supplier.Approve([]);
        var updateReTriggered = supplier.UpdateBankAccount(account.Id, "New Holder", "Bank", null, null, null, null, "SYP", isComplianceCritical: true);
        updateReTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);

        supplier.Approve([]);
        var removeReTriggered = supplier.RemoveBankAccount(account.Id, isComplianceCritical: true);
        removeReTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void LinkCategory_and_UnlinkCategory_report_the_same_signal()
    {
        var supplier = SupplierTestFactory.Approved();

        var (link, linkReTriggered) = supplier.LinkCategory("logistics", isComplianceCritical: true);
        link.Should().NotBeNull();
        linkReTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);

        supplier.Approve([]);
        var unlinkReTriggered = supplier.UnlinkCategory("logistics", isComplianceCritical: true);
        unlinkReTriggered.Should().BeTrue();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void LinkCategory_still_reports_the_signal_even_when_the_link_itself_is_a_no_op()
    {
        // The already-linked-is-a-no-op path returns (null, reTriggered) - proving the tuple's
        // second element isn't accidentally tied to whether a link was actually created.
        var supplier = SupplierTestFactory.Approved();

        var (link, reTriggered) = supplier.LinkCategory("catering", isComplianceCritical: true);

        link.Should().BeNull("catering was already linked in SupplierTestFactory.Approved's setup");
        reTriggered.Should().BeTrue("EnsureEditableForComplianceField runs before the already-linked check");
    }
}
