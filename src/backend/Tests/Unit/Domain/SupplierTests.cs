using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

public class SupplierTests
{
    private static Supplier CreateDraftSupplier() => Supplier.Register(
        "SUP-2026-000001", "شركة الاختبار", "Test Co", "CR-1", "Zaid", "zaid@example.com");

    [Fact]
    public void Register_creates_supplier_in_draft_with_one_primary_representative()
    {
        var supplier = CreateDraftSupplier();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Draft);
        supplier.Representatives.Should().ContainSingle(r => r.IsPrimary);
    }

    [Fact]
    public void MarkEmailVerified_from_draft_transitions_to_email_verified()
    {
        var supplier = CreateDraftSupplier();

        supplier.MarkEmailVerified();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.EmailVerified);
    }

    [Fact]
    public void MarkEmailVerified_when_not_draft_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        var act = () => supplier.MarkEmailVerified();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_when_not_under_review_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();

        var act = () => supplier.Approve([]);

        act.Should().Throw<DomainException>();
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.None);
    }

    [Fact]
    public void UpdateProfile_from_EmailVerified_transitions_to_ProfileInProgress()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        supplier.UpdateProfile("CR-1", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress);
        supplier.AddressLine.Should().Be("123 Main St");
    }

    [Fact]
    public void UpdateProfile_before_email_verified_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();

        var act = () => supplier.UpdateProfile("CR-1", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateProfile_after_submission_is_rejected_by_the_domain_read_only()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateProfile("CR-1", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");
        supplier.Representatives[0].Phone = "+963000000";
        supplier.Submit([]);

        var act = () => supplier.UpdateProfile("CR-1", "TAX-1", "changed", "Damascus", "Syria", "SYP");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void GetMissingProfileFields_lists_every_unset_required_field()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        var missing = supplier.GetMissingProfileFields();

        missing.Should().Contain(["taxId", "addressLine", "city", "country", "currencyCode", "primaryContactPhone"]);
        missing.Should().NotContain("registrationNumber"); // set at Register()
    }

    [Fact]
    public void Submit_with_incomplete_profile_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateProfile("CR-1", null, null, null, null, null);

        var act = () => supplier.Submit([]);

        act.Should().Throw<DomainException>().WithMessage("*missing*");
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress);
    }

    [Fact]
    public void Submit_with_complete_profile_transitions_to_Submitted()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateProfile("CR-1", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");
        supplier.Representatives[0].Phone = "+963000000";

        supplier.Submit([]);

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Submitted);
        supplier.GetMissingProfileFields().Should().BeEmpty();
    }

    private static Supplier CreateSubmittedSupplier()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateProfile("CR-1", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");
        supplier.Representatives[0].Phone = "+963000000";
        supplier.Submit([]);
        return supplier;
    }

    [Fact]
    public void PickUpForReview_from_Submitted_transitions_to_UnderReview()
    {
        var supplier = CreateSubmittedSupplier();

        supplier.PickUpForReview();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void PickUpForReview_when_not_Submitted_or_Resubmitted_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();

        var act = supplier.PickUpForReview;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_with_no_blocking_documents_transitions_to_Approved_and_Active()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();

        supplier.Approve([]);

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Approved);
        supplier.LifecycleState.Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public void Approve_with_blocking_documents_is_rejected_by_the_domain()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();

        var act = () => supplier.Approve(["tax_certificate"]);

        act.Should().Throw<DomainException>();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void Reject_without_a_reason_is_rejected_by_the_domain()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();

        var act = () => supplier.Reject("");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reject_with_a_reason_transitions_to_Rejected()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();

        supplier.Reject("Missing legal documentation");

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Rejected);
    }

    [Fact]
    public void RequestInfo_from_UnderReview_transitions_to_InfoRequested()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();

        supplier.RequestInfo();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.InfoRequested);
    }

    [Fact]
    public void UpdateProfile_while_InfoRequested_is_allowed()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        var act = () => supplier.UpdateProfile("CR-2", "TAX-1", "123 Main St", "Damascus", "Syria", "SYP");

        act.Should().NotThrow();
        supplier.RegistrationNumber.Should().Be("CR-2");
    }

    [Fact]
    public void Resubmit_from_InfoRequested_transitions_to_Resubmitted()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        supplier.Resubmit();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Resubmitted);
    }

    [Fact]
    public void Resubmit_when_not_InfoRequested_is_rejected_by_the_domain()
    {
        var supplier = CreateSubmittedSupplier();

        var act = supplier.Resubmit;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void PickUpForReview_after_Resubmit_returns_to_UnderReview_for_the_review_loop()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();
        supplier.Resubmit();

        supplier.PickUpForReview();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }
}
