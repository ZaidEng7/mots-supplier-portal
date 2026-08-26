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
}
