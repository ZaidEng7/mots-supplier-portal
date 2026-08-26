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

        var act = supplier.Approve;

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
        supplier.Submit();

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

        var act = supplier.Submit;

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

        supplier.Submit();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Submitted);
        supplier.GetMissingProfileFields().Should().BeEmpty();
    }
}
