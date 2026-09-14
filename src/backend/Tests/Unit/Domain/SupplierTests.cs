// The supplier record: registration, the profile, the submit gate, and resubmission.
//
// The shared setup fills every field the submit gate checks, including the written minimums, mirroring the shape
// a real onboarding flow ends up with.
//
//
// RESUBMISSION TAKES ITS GATE AS A REQUIRED ARGUMENT
//
// It used to take none, which made it a second entrance to review with no gate on it.
//
// A supplier could re-upload a required document, superseding the approved version and leaving the latest one
// unscanned, resubmit through that door, and be approved holding a document nobody had scanned.
//
// The argument is required rather than optional so a caller cannot forget it.
//
//
// WHAT RESUBMISSION IS SCOPED TO, AND WHAT SUBMISSION IS NOT
//
// A document rejected independently of the information request, through the separate per-document review action,
// must not block resolving what the open request actually flagged.
//
// So only the flagged documents and fields gate a resubmission. Submission, by contrast, stays fully unscoped.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

public class SupplierTests
{
    private static Supplier CreateDraftSupplier() => Supplier.Register(
        "SUP-2026-000001", "شركة الاختبار", "Test Co", "CR-1", "Zaid", "zaid@example.com");

    private static void CompleteProfile(Supplier supplier)
    {
        supplier.UpdateCoreProfile("A tourism supplier", "https://example.com", "SME", "SYP");
        supplier.UpdateLegalInfo("شركة الاختبار", "Test Co", "CR-1", "TAX-1", SupplierLegalType.Company, null, isComplianceCritical: true);
        supplier.AddAddress(AddressKind.HeadOffice, "123 Main St", null, "Damascus", "DIM", "Syria", null, null, null);
        supplier.LinkCategory("catering", isComplianceCritical: true);
        supplier.Representatives[0].Phone = "+963000000";
    }

    private static Supplier CreateProfileInProgressSupplier()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        CompleteProfile(supplier);
        return supplier;
    }

    private static Supplier CreateSubmittedSupplier()
    {
        var supplier = CreateProfileInProgressSupplier();
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);
        supplier.Submit([]);
        return supplier;
    }

    [Fact]
    public void Register_creates_supplier_in_draft_with_one_primary_representative()
    {
        var supplier = CreateDraftSupplier();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Draft);
        supplier.Representatives.Should().ContainSingle(r => r.IsPrimary);
        supplier.LegalInfo.Should().NotBeNull();
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
    public void UpdateCoreProfile_from_EmailVerified_transitions_to_ProfileInProgress()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        supplier.UpdateCoreProfile("desc", "https://example.com", "SME", "SYP");

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress);
        supplier.CurrencyCode.Should().Be("SYP");
    }

    [Fact]
    public void UpdateCoreProfile_before_email_verified_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();

        var act = () => supplier.UpdateCoreProfile("desc", null, null, "SYP");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateCoreProfile_after_submission_is_rejected_by_the_domain_read_only()
    {
        var supplier = CreateSubmittedSupplier();

        var act = () => supplier.UpdateCoreProfile("changed", null, null, "SYP");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void GetMissingProfileFields_lists_every_unset_required_field()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        var missing = supplier.GetMissingProfileFields();

        missing.Should().Contain(["currencyCode", "address", "categoryLink", "primaryContactPhone", "termsAccepted"]);
        missing.Should().NotContain("legalInfo"); // set at Register() from the trade name
    }

    [Fact]
    public void Submit_with_incomplete_profile_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile(null, null, null, null);

        var act = () => supplier.Submit([]);

        act.Should().Throw<DomainException>().WithMessage("*missing*");
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress);
    }

    [Fact]
    public void Submit_with_complete_profile_transitions_to_Submitted()
    {
        var supplier = CreateProfileInProgressSupplier();
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        supplier.Submit([]);

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Submitted);
        supplier.GetMissingProfileFields().Should().BeEmpty();
    }

    [Fact]
    public void Submit_without_at_least_one_address_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile(null, null, null, "SYP");
        supplier.LinkCategory("catering", isComplianceCritical: true);
        supplier.Representatives[0].Phone = "+963000000";
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        var act = () => supplier.Submit([]);

        act.Should().Throw<DomainException>().WithMessage("*address*");
    }

    [Fact]
    public void Submit_without_at_least_one_category_link_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile(null, null, null, "SYP");
        supplier.AddAddress(AddressKind.HeadOffice, "123 Main St", null, "Damascus", "DIM", "Syria", null, null, null);
        supplier.Representatives[0].Phone = "+963000000";
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        var act = () => supplier.Submit([]);

        act.Should().Throw<DomainException>().WithMessage("*categoryLink*");
    }

    [Fact]
    public void Submit_without_accepting_terms_is_rejected_by_the_domain()
    {
        var supplier = CreateProfileInProgressSupplier();

        var act = () => supplier.Submit([]);

        act.Should().Throw<DomainException>().WithMessage("*termsAccepted*");
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.ProfileInProgress);
    }

    [Fact]
    public void AcceptTerms_records_version_and_timestamp()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        supplier.TermsAcceptedVersion.Should().Be(Supplier.CurrentTermsVersion);
        supplier.TermsAcceptedAt.Should().NotBeNull();
        supplier.GetMissingProfileFields().Should().NotContain("termsAccepted");
    }

    [Fact]
    public void AcceptTerms_before_email_verified_is_rejected_by_the_domain()
    {
        var supplier = CreateDraftSupplier();

        var act = () => supplier.AcceptTerms(Supplier.CurrentTermsVersion);

        act.Should().Throw<DomainException>();
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
    public void UpdateCoreProfile_while_InfoRequested_is_allowed()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        var act = () => supplier.UpdateCoreProfile("changed", null, null, "USD");

        act.Should().NotThrow();
        supplier.CurrencyCode.Should().Be("USD");
    }

    [Fact]
    public void Resubmit_from_InfoRequested_transitions_to_Resubmitted()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        supplier.Resubmit([], [], []);

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Resubmitted);
    }

    [Fact]
    public void Resubmit_when_not_InfoRequested_is_rejected_by_the_domain()
    {
        var supplier = CreateSubmittedSupplier();

        var act = () => supplier.Resubmit([], [], []);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Resubmit_refuses_when_a_required_document_is_outstanding()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        var act = () => supplier.Resubmit(["tax_certificate"], [], ["tax_certificate"]);

        act.Should().Throw<DomainException>().WithMessage("*missing required items*tax_certificate*");
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.InfoRequested,
            "a refused resubmit must leave the supplier where it was, not half-way through");
    }

    [Fact]
    public void Resubmit_ignores_an_outstanding_document_that_was_not_flagged()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();

        var act = () => supplier.Resubmit(["tax_certificate"], [], []);

        act.Should().NotThrow();
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Resubmitted);
    }

    [Fact]
    public void PickUpForReview_after_Resubmit_returns_to_UnderReview_for_the_review_loop()
    {
        var supplier = CreateSubmittedSupplier();
        supplier.PickUpForReview();
        supplier.RequestInfo();
        supplier.Resubmit([], [], []);

        supplier.PickUpForReview();

        supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview);
    }

    [Fact]
    public void AddAddress_first_one_becomes_primary_and_removing_it_promotes_the_next()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        var first = supplier.AddAddress(AddressKind.HeadOffice, "A", null, "Damascus", "DIM", "Syria", null, null, null);
        var second = supplier.AddAddress(AddressKind.Branch, "B", null, "Aleppo", "ALP", "Syria", null, null, null);

        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse();

        supplier.RemoveAddress(first.Id);

        supplier.Addresses.Should().ContainSingle(a => a.Id == second.Id && a.IsPrimary);
    }

    [Fact]
    public void LinkCategory_is_idempotent()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        supplier.LinkCategory("catering", isComplianceCritical: true);
        supplier.LinkCategory("catering", isComplianceCritical: true);

        supplier.CategoryLinks.Should().ContainSingle();
    }

    [Fact]
    public void AddBankAccount_stores_only_the_encrypted_and_masked_values_never_plaintext()
    {
        var supplier = CreateDraftSupplier();
        supplier.MarkEmailVerified();

        var (account, _) = supplier.AddBankAccount("Test Co", "Test Bank", null, [1, 2, 3], "****1234", null, "SYP", isComplianceCritical: true);

        supplier.BankAccounts.Should().ContainSingle();
        account.MaskedAccountNumber.Should().Be("****1234");
    }
}
