// The six compliance-critical domain methods return the SAME fact the guard computes internally.
//
// Rather than a caller having to re-derive an approximation of it later by comparing the state before and after,
// which is what the retrigger recorder did before this fix. That was correct only because no other code path
// could produce the same transition for a different reason, and the recorder's own header explains the rest.
//
//
// THESE ARE ORDINARY BEHAVIOURAL ASSERTIONS, NOT A DEMONSTRATION OF LIVE DIVERGENCE
//
// There is no second code path in today's domain model that WOULD diverge from the old heuristic, which is
// exactly the point: the old code's correctness could not be exercised into failing today, only argued about.
//
// What these prove instead is that the return value now IS the domain's own answer rather than a proxy for it.
// Reverting any of the six methods to discard the guard's result makes this file fail to COMPILE, which is the
// strongest form of revert-to-red available for a change to the shape of an interface.
//
//
// TWO CASES WORTH NAMING
//
// An approved supplier editing a field that is not compliance-critical is not a case the guard ever answers
// false for: it falls through to the ordinary editability check, which refuses such an edit on an approved
// supplier outright, because there is nothing to retrigger FROM. The false case is reachable before approval,
// where ordinary edits are still allowed.
//
// And the already-linked path, where linking a category is a no-op, still returns the retrigger fact. That proves
// the second element of the pair is not accidentally tied to whether a link was actually created.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

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
        var supplier = SupplierTestFactory.Approved();

        var (link, reTriggered) = supplier.LinkCategory("catering", isComplianceCritical: true);

        link.Should().BeNull("catering was already linked in SupplierTestFactory.Approved's setup");
        reTriggered.Should().BeTrue("EnsureEditableForComplianceField runs before the already-linked check");
    }
}
