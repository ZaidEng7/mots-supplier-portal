namespace MotsSupplierPortal.Domain.Suppliers;

public enum SupplierSyncStatus
{
    Pending,
    Synced,
    Failed,
}

/// <summary>
/// Central supplier master the portal owns until ERP approval (docs/architecture/DOMAIN-MODEL.md §5.3).
/// The domain — not the API, not the UI — is the sole authority on legal state transitions.
/// </summary>
public sealed class Supplier
{
    private readonly List<Representative> _representatives = [];
    private readonly List<Address> _addresses = [];
    private readonly List<Contact> _contacts = [];
    private readonly List<Branch> _branches = [];
    private readonly List<BankAccount> _bankAccounts = [];
    private readonly List<CategoryLink> _categoryLinks = [];

    public Guid Id { get; private init; }
    public string ReferenceCode { get; private init; } = null!;
    public string DisplayNameAr { get; private set; } = null!;
    public string DisplayNameEn { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Website { get; private set; }
    public string? LogoStorageKey { get; private set; }
    public string? SupplierGroup { get; private set; }
    public string? CurrencyCode { get; private set; }
    public LegalInfo? LegalInfo { get; private set; }
    public SupplierOnboardingState OnboardingState { get; private set; }
    public SupplierLifecycleState LifecycleState { get; private set; } = SupplierLifecycleState.None;
    public string? ExternalId { get; private set; }
    public SupplierSyncStatus SyncStatus { get; private set; } = SupplierSyncStatus.Pending;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? TermsAcceptedVersion { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public uint RowVersion { get; private set; }

    /// <summary>BRULE-009: T&C content is owned by business; version string is an
    /// [ASSUMPTION] placeholder until that content and its versioning process exist.</summary>
    public const string CurrentTermsVersion = "1.0";

    public IReadOnlyList<Representative> Representatives => _representatives;
    public IReadOnlyList<Address> Addresses => _addresses;
    public IReadOnlyList<Contact> Contacts => _contacts;
    public IReadOnlyList<Branch> Branches => _branches;
    public IReadOnlyList<BankAccount> BankAccounts => _bankAccounts;
    public IReadOnlyList<CategoryLink> CategoryLinks => _categoryLinks;

    private Supplier() { }

    /// <summary>
    /// Registers a new prospective supplier. Legal identifiers are captured generically —
    /// no invented Syrian validation rules (docs/product/ASSUMPTIONS.md ASM-020). LegalInfo is
    /// seeded with the trade name as an initial legal name (supplier can distinguish them later
    /// via UpdateLegalInfo); RegistrationNumber lives on LegalInfo, not as a Register() param.
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
            OnboardingState = SupplierOnboardingState.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        supplier.LegalInfo = Domain.Suppliers.LegalInfo.Create(
            displayNameAr, displayNameEn, registrationNumber, taxId: null, SupplierLegalType.Company, establishedOn: null);

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

    private void EnsureEditable()
    {
        if (OnboardingState is not (SupplierOnboardingState.EmailVerified or SupplierOnboardingState.ProfileInProgress or SupplierOnboardingState.InfoRequested))
        {
            throw new DomainException(
                $"Cannot edit profile from state '{OnboardingState}'; only 'EmailVerified', 'ProfileInProgress', or 'InfoRequested' allow edits.");
        }
    }

    private void AdvancePastEmailVerified()
    {
        if (OnboardingState == SupplierOnboardingState.EmailVerified)
        {
            OnboardingState = SupplierOnboardingState.ProfileInProgress;
        }
    }

    /// <summary>FEAT-04.9/STORY-04.9.1: unlike other profile fields, a compliance-critical one
    /// (legal id, bank account, category links, per SupplierFieldConfig - the caller resolves
    /// <paramref name="isComplianceCritical"/> from that config, the domain itself doesn't know
    /// about config/infrastructure) is editable even once Approved, because otherwise a
    /// legitimately-changed bank account could never be updated post-approval at all. Editing one
    /// while Approved re-triggers review (back to UnderReview) rather than silently accepting the
    /// change - LifecycleState stays Active during the re-review window, the supplier isn't
    /// suspended just for having an edit pending. When the field's re-trigger is disabled in
    /// config, it behaves like any other profile field: normal EnsureEditable gating, blocked once
    /// Approved.</summary>
    private bool EnsureEditableForComplianceField(bool isComplianceCritical)
    {
        if (isComplianceCritical && OnboardingState == SupplierOnboardingState.Approved)
        {
            OnboardingState = SupplierOnboardingState.UnderReview;
            return true;
        }

        EnsureEditable();
        return false;
    }

    /// <summary>FEAT-04.1 core profile fields (description/website/group/currency). Editable while
    /// EmailVerified/ProfileInProgress, or InfoRequested (field-level restriction while
    /// InfoRequested is enforced by the handler, which knows the active annotation - the domain
    /// only gates which *states* allow editing at all). The first EmailVerified call advances to
    /// ProfileInProgress.</summary>
    public void UpdateCoreProfile(string? description, string? website, string? supplierGroup, string? currencyCode)
    {
        EnsureEditable();
        Description = description;
        Website = website;
        SupplierGroup = supplierGroup;
        CurrencyCode = currencyCode;
        AdvancePastEmailVerified();
    }

    public void SetLogo(string storageKey)
    {
        EnsureEditable();
        LogoStorageKey = storageKey;
    }

    /// <summary>FEAT-04.2/FR-PROF-002. Compliance-critical (FEAT-04.9): editing legal id
    /// post-Approval re-triggers review.</summary>
    public void UpdateLegalInfo(string legalNameAr, string legalNameEn, string? registrationNumber, string? taxId, SupplierLegalType supplierType, DateOnly? establishedOn, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        LegalInfo = Domain.Suppliers.LegalInfo.Create(legalNameAr, legalNameEn, registrationNumber, taxId, supplierType, establishedOn);
        AdvancePastEmailVerified();
    }

    /// <summary>FEAT-04.4/FR-PROF-004: a new representative is never primary by construction -
    /// the caller must explicitly SetPrimaryRepresentative if they want to reassign it.</summary>
    public Representative AddRepresentative(string fullName, string email, string? phone, string? position)
    {
        EnsureEditable();
        var representative = new Representative
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            FullName = fullName,
            Email = email,
            Phone = phone,
            Position = position,
            IsPrimary = false,
        };
        _representatives.Add(representative);
        return representative;
    }

    public void UpdateRepresentative(Guid representativeId, string fullName, string email, string? phone, string? position)
    {
        EnsureEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        representative.FullName = fullName;
        representative.Email = email;
        representative.Phone = phone;
        representative.Position = position;
    }

    /// <summary>DOMAIN-MODEL.md §5.3: exactly one primary representative at all times - the last
    /// remaining representative can never be removed (there would be nobody left to be primary),
    /// and removing the primary while others remain auto-promotes the next one so the invariant
    /// holds continuously, not just "by construction" at registration.</summary>
    public void RemoveRepresentative(Guid representativeId)
    {
        EnsureEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        if (_representatives.Count == 1)
        {
            throw new DomainException("Cannot remove the last remaining representative - a supplier must always have at least one.");
        }

        _representatives.Remove(representative);
        if (representative.IsPrimary)
        {
            _representatives[0].IsPrimary = true;
        }
    }

    /// <summary>DOMAIN-MODEL.md §5.3 invariant example: supplier.SetPrimaryRepresentative(id).</summary>
    public void SetPrimaryRepresentative(Guid representativeId)
    {
        EnsureEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        foreach (var r in _representatives) r.IsPrimary = false;
        representative.IsPrimary = true;
    }

    public Address AddAddress(AddressKind kind, string line1, string? line2, string city, string regionCode, string country, string? postalCode, double? latitude, double? longitude)
    {
        EnsureEditable();
        var address = new Address
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            Kind = kind,
            Line1 = line1,
            Line2 = line2,
            City = city,
            RegionCode = regionCode,
            Country = country,
            PostalCode = postalCode,
            Latitude = latitude,
            Longitude = longitude,
            IsPrimary = _addresses.Count == 0,
        };
        _addresses.Add(address);
        AdvancePastEmailVerified();
        return address;
    }

    public void UpdateAddress(Guid addressId, AddressKind kind, string line1, string? line2, string city, string regionCode, string country, string? postalCode, double? latitude, double? longitude)
    {
        EnsureEditable();
        var address = _addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new DomainException("Address not found.");
        address.Kind = kind;
        address.Line1 = line1;
        address.Line2 = line2;
        address.City = city;
        address.RegionCode = regionCode;
        address.Country = country;
        address.PostalCode = postalCode;
        address.Latitude = latitude;
        address.Longitude = longitude;
    }

    public void RemoveAddress(Guid addressId)
    {
        EnsureEditable();
        var address = _addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new DomainException("Address not found.");
        _addresses.Remove(address);
        if (address.IsPrimary && _addresses.Count > 0)
        {
            _addresses[0].IsPrimary = true;
        }
    }

    public Contact AddContact(string fullName, string email, string? phone, string? role)
    {
        EnsureEditable();
        var contact = new Contact { Id = Guid.CreateVersion7(), SupplierId = Id, FullName = fullName, Email = email, Phone = phone, Role = role };
        _contacts.Add(contact);
        return contact;
    }

    public void UpdateContact(Guid contactId, string fullName, string email, string? phone, string? role)
    {
        EnsureEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        contact.FullName = fullName;
        contact.Email = email;
        contact.Phone = phone;
        contact.Role = role;
    }

    public void RemoveContact(Guid contactId)
    {
        EnsureEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        _contacts.Remove(contact);
    }

    /// <summary>FEAT-04.5: AddressId, when given, must be one of this supplier's own addresses -
    /// otherwise a branch could point at another supplier's address or a nonexistent one.</summary>
    private void EnsureAddressBelongsToThisSupplier(Guid? addressId)
    {
        if (addressId is not null && !_addresses.Any(a => a.Id == addressId))
        {
            throw new DomainException("AddressId does not belong to this supplier.");
        }
    }

    public Branch AddBranch(string nameAr, string nameEn, Guid? addressId)
    {
        EnsureEditable();
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = new Branch { Id = Guid.CreateVersion7(), SupplierId = Id, NameAr = nameAr, NameEn = nameEn, AddressId = addressId };
        _branches.Add(branch);
        return branch;
    }

    public void UpdateBranch(Guid branchId, string nameAr, string nameEn, Guid? addressId, bool isActive)
    {
        EnsureEditable();
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        branch.NameAr = nameAr;
        branch.NameEn = nameEn;
        branch.AddressId = addressId;
        branch.IsActive = isActive;
    }

    public void RemoveBranch(Guid branchId)
    {
        EnsureEditable();
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        _branches.Remove(branch);
    }

    /// <summary>FR-PROF-006: BankAccount.EncryptedAccountNumber/MaskedAccountNumber are computed
    /// by the caller (handler has FieldEncryptionService, the domain does not depend on
    /// Infrastructure) and passed in already encrypted/masked. DOMAIN-MODEL.md: the first bank
    /// account added is automatically the default - exactly one default whenever any exist.</summary>
    public BankAccount AddBankAccount(string accountHolderName, string bankName, string? branchName, byte[] encryptedAccountNumber, string maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        var account = new BankAccount
        {
            Id = Guid.CreateVersion7(),
            SupplierId = Id,
            AccountHolderName = accountHolderName,
            BankName = bankName,
            BranchName = branchName,
            EncryptedAccountNumber = encryptedAccountNumber,
            MaskedAccountNumber = maskedAccountNumber,
            SwiftBic = swiftBic,
            CurrencyCode = currencyCode,
            IsDefault = _bankAccounts.Count == 0,
        };
        _bankAccounts.Add(account);
        return account;
    }

    /// <summary>AccountNumber fields are null when the caller isn't changing the account number
    /// (handler re-encrypts and passes non-null values only when the account number is actually
    /// being changed).</summary>
    public void UpdateBankAccount(Guid bankAccountId, string accountHolderName, string bankName, string? branchName, byte[]? encryptedAccountNumber, string? maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        account.AccountHolderName = accountHolderName;
        account.BankName = bankName;
        account.BranchName = branchName;
        account.SwiftBic = swiftBic;
        account.CurrencyCode = currencyCode;
        if (encryptedAccountNumber is not null && maskedAccountNumber is not null)
        {
            account.EncryptedAccountNumber = encryptedAccountNumber;
            account.MaskedAccountNumber = maskedAccountNumber;
        }
    }

    public void RemoveBankAccount(Guid bankAccountId, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        _bankAccounts.Remove(account);
        if (account.IsDefault && _bankAccounts.Count > 0)
        {
            _bankAccounts[0].IsDefault = true;
        }
    }

    /// <summary>DOMAIN-MODEL.md: lets the supplier explicitly pick which bank account is default,
    /// on top of the automatic first-added/reassign-on-remove behavior in AddBankAccount/
    /// RemoveBankAccount.</summary>
    public void SetDefaultBankAccount(Guid bankAccountId)
    {
        EnsureEditable();
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        foreach (var b in _bankAccounts) b.IsDefault = false;
        account.IsDefault = true;
    }

    public CategoryLink? LinkCategory(string categoryCode, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        if (_categoryLinks.Any(l => l.CategoryCode == categoryCode)) return null;
        var link = new CategoryLink { Id = Guid.CreateVersion7(), SupplierId = Id, CategoryCode = categoryCode };
        _categoryLinks.Add(link);
        return link;
    }

    public void UnlinkCategory(string categoryCode, bool isComplianceCritical)
    {
        EnsureEditableForComplianceField(isComplianceCritical);
        var link = _categoryLinks.FirstOrDefault(l => l.CategoryCode == categoryCode);
        if (link is not null) _categoryLinks.Remove(link);
    }

    /// <summary>
    /// Core-profile completeness (STORY-03.1.1 AC1/AC2) plus BRULE-009's T&C-acceptance gate and
    /// EPIC-04's Address/CategoryLink minimums (STORY-04.3.1/STORY-04.7.1, per product-owner
    /// decision 2026-08-27: >=1 CategoryLink is a hard submit requirement of EPIC-04 itself).
    /// </summary>
    public IReadOnlyList<string> GetMissingProfileFields()
    {
        var missing = new List<string>();
        if (LegalInfo is null || string.IsNullOrWhiteSpace(LegalInfo.LegalNameAr) || string.IsNullOrWhiteSpace(LegalInfo.LegalNameEn))
        {
            missing.Add("legalInfo");
        }
        if (string.IsNullOrWhiteSpace(CurrencyCode)) missing.Add("currencyCode");
        if (!_addresses.Any(a => a.Kind == AddressKind.HeadOffice)) missing.Add("address");
        if (_categoryLinks.Count == 0) missing.Add("categoryLink");
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

    /// <summary>FEAT-04.10/FR-PROF-010: written only by the (not-yet-built) Outbox-consumer path
    /// once a real ERP integration exists - never directly settable via an API endpoint.</summary>
    public void MarkSynced(string externalId)
    {
        ExternalId = externalId;
        SyncStatus = SupplierSyncStatus.Synced;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSyncFailed()
    {
        SyncStatus = SupplierSyncStatus.Failed;
    }
}
