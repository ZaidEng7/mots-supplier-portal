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
    public string? AddressLine { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? CurrencyCode { get; private set; }
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
        string primaryRepresentativeEmail,
        string? primaryRepresentativePhone = null)
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
            Phone = primaryRepresentativePhone,
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

    /// <summary>
    /// FEAT-04.1/04.3 core profile fields, editable while EmailVerified or ProfileInProgress
    /// (STORY-03.1.1 AC3: the application becomes read-only once Submitted). The first call
    /// advances EmailVerified -> ProfileInProgress.
    /// </summary>
    public void UpdateProfile(string? registrationNumber, string? taxId, string? addressLine, string? city, string? country, string? currencyCode)
    {
        if (OnboardingState is not (SupplierOnboardingState.EmailVerified or SupplierOnboardingState.ProfileInProgress))
        {
            throw new DomainException(
                $"Cannot edit profile from state '{OnboardingState}'; only 'EmailVerified' or 'ProfileInProgress' allow edits.");
        }

        RegistrationNumber = registrationNumber;
        TaxId = taxId;
        AddressLine = addressLine;
        City = city;
        Country = country;
        CurrencyCode = currencyCode;

        if (OnboardingState == SupplierOnboardingState.EmailVerified)
        {
            OnboardingState = SupplierOnboardingState.ProfileInProgress;
        }
    }

    /// <summary>
    /// Core-profile completeness (STORY-03.1.1 AC1/AC2). Document requirements (EPIC-05) are not
    /// yet part of this evaluation - MSP-49 will fold them in once document upload exists.
    /// </summary>
    public IReadOnlyList<string> GetMissingProfileFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(RegistrationNumber)) missing.Add("registrationNumber");
        if (string.IsNullOrWhiteSpace(TaxId)) missing.Add("taxId");
        if (string.IsNullOrWhiteSpace(AddressLine)) missing.Add("addressLine");
        if (string.IsNullOrWhiteSpace(City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(Country)) missing.Add("country");
        if (string.IsNullOrWhiteSpace(CurrencyCode)) missing.Add("currencyCode");
        if (_representatives.Any(r => r.IsPrimary && string.IsNullOrWhiteSpace(r.Phone)) || _representatives.All(r => !r.IsPrimary))
        {
            missing.Add("primaryContactPhone");
        }
        return missing;
    }

    /// <summary>ProfileInProgress -> Submitted. Refuses the transition server-side if the
    /// checklist is incomplete - the UI cannot bypass this (STORY-03.1.1 AC2).</summary>
    public void Submit()
    {
        if (OnboardingState != SupplierOnboardingState.ProfileInProgress)
        {
            throw new DomainException(
                $"Cannot submit from state '{OnboardingState}'; only 'ProfileInProgress' is valid.");
        }

        var missing = GetMissingProfileFields();
        if (missing.Count > 0)
        {
            throw new DomainException($"Cannot submit: missing required fields: {string.Join(", ", missing)}.");
        }

        OnboardingState = SupplierOnboardingState.Submitted;
    }
}
