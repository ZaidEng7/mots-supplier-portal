namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// Central supplier master the portal owns until ERP approval (docs/architecture/DOMAIN-MODEL.md §5.3).
/// The domain — not the API, not the UI — is the sole authority on legal state transitions.
/// </summary>
public sealed class Supplier
{
    private readonly List<Representative> _representatives = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public string DisplayNameAr { get; private set; } = null!;
    public string DisplayNameEn { get; private set; } = null!;
    public string? RegistrationNumber { get; private set; }
    public string? TaxId { get; private set; }
    public SupplierOnboardingState OnboardingState { get; private set; }
    public SupplierLifecycleState LifecycleState { get; private set; } = SupplierLifecycleState.None;
    public string? ExternalId { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<Representative> Representatives => _representatives;

    private Supplier() { }

    /// <summary>
    /// Registers a new prospective supplier. Legal identifiers are captured generically —
    /// no invented Syrian validation rules (docs/product/ASSUMPTIONS.md ASM-020).
    /// </summary>
    public static Supplier Register(
        string referenceCode,
        string displayNameAr,
        string displayNameEn,
        string? registrationNumber,
        string primaryRepresentativeName,
        string primaryRepresentativeEmail)
    {
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            DisplayNameAr = displayNameAr,
            DisplayNameEn = displayNameEn,
            RegistrationNumber = registrationNumber,
            OnboardingState = SupplierOnboardingState.Draft,
        };

        supplier._representatives.Add(new Representative
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplier.Id,
            FullName = primaryRepresentativeName,
            Email = primaryRepresentativeEmail,
            IsPrimary = true,
        });

        return supplier;
    }

    /// <summary>Draft -> EmailVerified. Onboarding cannot progress past this until verified.</summary>
    public void MarkEmailVerified()
    {
        if (OnboardingState != SupplierOnboardingState.Draft)
        {
            throw new DomainException(
                $"Cannot mark email verified from state '{OnboardingState}'; only 'Draft' is valid.");
        }

        OnboardingState = SupplierOnboardingState.EmailVerified;
    }

    /// <summary>
    /// UnderReview -> Approved. Only reachable via reviewer action carrying supplier.approve
    /// permission (enforced at the API); raises the ERP supplier-master sync obligation.
    /// </summary>
    public void Approve()
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot approve from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        OnboardingState = SupplierOnboardingState.Approved;
        LifecycleState = SupplierLifecycleState.Active;
    }

    public bool IsEmailVerifiedOrLater =>
        OnboardingState is not SupplierOnboardingState.Draft;
}
