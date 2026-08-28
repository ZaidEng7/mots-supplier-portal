using System.Reflection;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>Builders for supplier aggregates in specific states (MSP-63).</summary>
internal static class SupplierTestFactory
{
    /// <summary>An approved, Active supplier built by driving the real transitions, so the starting
    /// point of the lifecycle tests is one the domain actually produces rather than one forced into
    /// place.</summary>
    public static Supplier Approved()
    {
        var supplier = Supplier.Register(
            $"SUP-2026-{Random.Shared.Next(1, 999_999):D6}", "شركة الاختبار", "Test Co",
            "CR-1", "Zaid", "zaid@example.com");

        supplier.MarkEmailVerified();
        supplier.UpdateCoreProfile("A tourism supplier", "https://example.com", "SME", "SYP");
        supplier.UpdateLegalInfo("شركة الاختبار", "Test Co", "CR-1", "TAX-1", SupplierLegalType.Company, null, isComplianceCritical: true);
        supplier.AddAddress(AddressKind.HeadOffice, "123 Main St", null, "Damascus", "DIM", "Syria", null, null, null);
        supplier.LinkCategory("catering", isComplianceCritical: true);
        supplier.Representatives[0].Phone = "+963000000";
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);
        supplier.Submit([]);
        supplier.PickUpForReview();
        supplier.Approve([]);

        return supplier;
    }

    /// <summary>
    /// A supplier forced into an arbitrary (onboarding, lifecycle) pair.
    ///
    /// Reflection is used deliberately and only here. The eligibility predicate must be asserted
    /// against EVERY combination, including ones the aggregate cannot currently reach - those are
    /// precisely the cases where a future transition could quietly make an ineligible supplier
    /// eligible. Driving real transitions can only produce the reachable subset, which would leave
    /// the interesting rows untested and turn the theory into a test that cannot fail.
    ///
    /// This does not weaken the domain's guarantees: the transition tests in
    /// <see cref="SupplierLifecycleTests"/> drive the real methods and assert what is refused.
    /// </summary>
    public static Supplier InState(SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle)
    {
        var supplier = Supplier.Register(
            $"SUP-2026-{Random.Shared.Next(1, 999_999):D6}", "شركة الاختبار", "Test Co",
            "CR-1", "Zaid", "zaid@example.com");

        Set(supplier, nameof(Supplier.OnboardingState), onboarding);
        Set(supplier, nameof(Supplier.LifecycleState), lifecycle);

        return supplier;
    }

    private static void Set(Supplier supplier, string propertyName, object value)
    {
        var property = typeof(Supplier).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{propertyName} not found on Supplier.");

        // Fails loudly if the property stops being settable, rather than silently leaving the
        // aggregate in its default state and asserting against the wrong thing.
        (property.SetMethod ?? throw new InvalidOperationException($"{propertyName} has no setter."))
            .Invoke(supplier, [value]);
    }
}
