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
    public string? TermsAcceptedVersion { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    /// <summary>BRULE-009: T&C content is owned by business; version string is an
    /// [ASSUMPTION] placeholder until that content and its versioning process exist.</summary>
    public const string CurrentTermsVersion = "1.0";

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
            CreatedAt = DateTimeOffset.UtcNow,
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

    public bool IsEmailVerifiedOrLater =>
        OnboardingState is not SupplierOnboardingState.Draft;

    /// <summary>
    /// FEAT-04.1/04.3 core profile fields, editable while EmailVerified/ProfileInProgress, or
    /// InfoRequested (STORY-03.3.1: only the fields flagged by the active
    /// SupplierReviewAnnotation may actually change while InfoRequested - that field-level
    /// restriction is enforced by the handler, which knows the annotation; the domain only gates
    /// which *states* allow editing at all). Read-only once Submitted/UnderReview/Approved/Rejected.
    /// The first EmailVerified call advances to ProfileInProgress.
    /// </summary>
    public void UpdateProfile(string? registrationNumber, string? taxId, string? addressLine, string? city, string? country, string? currencyCode)
    {
        if (OnboardingState is not (SupplierOnboardingState.EmailVerified or SupplierOnboardingState.ProfileInProgress or SupplierOnboardingState.InfoRequested))
        {
            throw new DomainException(
                $"Cannot edit profile from state '{OnboardingState}'; only 'EmailVerified', 'ProfileInProgress', or 'InfoRequested' allow edits.");
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
    /// Core-profile completeness (STORY-03.1.1 AC1/AC2) plus BRULE-009's T&C-acceptance gate.
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
        if (TermsAcceptedAt is null) missing.Add("termsAccepted");
        return missing;
    }

    /// <summary>BRULE-009: records T&C acceptance with the version and a timestamp - the consent
    /// record the submit gate checks for. Accepting again (e.g. after a later version ships)
    /// simply overwrites the record; only the latest acceptance needs to be current at submit
    /// time, matching the rule's "before first submission" wording.</summary>
    public void AcceptTerms(string version)
    {
        if (OnboardingState is SupplierOnboardingState.Draft)
        {
            throw new DomainException("Cannot accept terms before the email is verified.");
        }

        TermsAcceptedVersion = version;
        TermsAcceptedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>ProfileInProgress -> Submitted. Refuses the transition server-side if the profile
    /// checklist is incomplete OR any required DocumentType lacks a satisfying uploaded version
    /// (docs/architecture/DOMAIN-MODEL.md §5.3 invariant) - the UI cannot bypass this (STORY-03.1.1
    /// AC2). <paramref name="missingRequiredDocumentTypeCodes"/> is computed by the handler, which
    /// owns the SupplierDocument query the aggregate itself doesn't have access to.</summary>
    public void Submit(IReadOnlyList<string> missingRequiredDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.ProfileInProgress)
        {
            throw new DomainException(
                $"Cannot submit from state '{OnboardingState}'; only 'ProfileInProgress' is valid.");
        }

        var missing = GetMissingProfileFields().Concat(missingRequiredDocumentTypeCodes).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException($"Cannot submit: missing required items: {string.Join(", ", missing)}.");
        }

        OnboardingState = SupplierOnboardingState.Submitted;
    }

    /// <summary>Submitted -> UnderReview. Reviewer picks up the application (STORY-03.2.1 AC1).</summary>
    public void PickUpForReview()
    {
        if (OnboardingState is not (SupplierOnboardingState.Submitted or SupplierOnboardingState.Resubmitted))
        {
            throw new DomainException(
                $"Cannot pick up for review from state '{OnboardingState}'; only 'Submitted' or 'Resubmitted' is valid.");
        }

        OnboardingState = SupplierOnboardingState.UnderReview;
    }

    /// <summary>
    /// UnderReview -> Approved -> Active. Only reachable via reviewer action carrying
    /// supplier.approve permission (enforced at the API); raises the ERP supplier-master sync
    /// obligation (FEAT-03.5). <paramref name="blockingRequiredDocumentTypeCodes"/> is the
    /// product-owner-decided approval gate (2026-08-26): approval is refused only if a required
    /// document is currently Rejected/ScanRejected/Expired - it does NOT require every document to
    /// already be individually Approved.
    /// </summary>
    public void Approve(IReadOnlyList<string> blockingRequiredDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot approve from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        if (blockingRequiredDocumentTypeCodes.Count > 0)
        {
            throw new DomainException(
                $"Cannot approve: required documents need attention: {string.Join(", ", blockingRequiredDocumentTypeCodes)}.");
        }

        OnboardingState = SupplierOnboardingState.Approved;
        LifecycleState = SupplierLifecycleState.Active;
    }

    /// <summary>UnderReview -> Rejected. Reason is mandatory (STORY-03.2.1 AC3).</summary>
    public void Reject(string reason)
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot reject from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }

        OnboardingState = SupplierOnboardingState.Rejected;
    }

    /// <summary>UnderReview -> InfoRequested (STORY-03.3.1 AC1). The annotation carrying the
    /// reason and flagged sections/documents is created by the handler.</summary>
    public void RequestInfo()
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot request info from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        OnboardingState = SupplierOnboardingState.InfoRequested;
    }

    /// <summary>InfoRequested -> Resubmitted (STORY-03.3.1 AC2), an intermediate, individually
    /// audited state before the handler immediately advances it back to UnderReview via
    /// <see cref="PickUpForReview"/> for the reviewer's next pass.</summary>
    public void Resubmit()
    {
        if (OnboardingState != SupplierOnboardingState.InfoRequested)
        {
            throw new DomainException(
                $"Cannot resubmit from state '{OnboardingState}'; only 'InfoRequested' is valid.");
        }

        OnboardingState = SupplierOnboardingState.Resubmitted;
    }
}
