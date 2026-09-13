// A supplier company: its profile, the people and places attached to it, where it is in registration
// and review, and whether it may currently trade.
//
// The portal owns this record until the ministry's finance system approves it.
//
// This record, not the API and not the interface, is the only authority on which state changes are
// legal.
//
//
// TWO STATE MACHINES, and they answer different questions
//
// OnboardingState is how far the company has got through registration and review, from draft to
// approved or rejected.
//
// LifecycleState is whether an approved supplier may currently trade: active, suspended or
// deactivated, and none at all before approval.
//
// IsEligibleToParticipate is the single answer to "may this supplier be invited to a tender or submit a
// bid?", and it requires both halves. Onboarding must have reached approved, because an applicant
// mid-review is not eligible however healthy its lifecycle looks, and the lifecycle must be active,
// because suspended and deactivated are both excluded from new selection while existing obligations are
// handled by policy elsewhere.
//
// It lives here on the record so that the tender side and the bid side cannot each invent their own
// eligibility rule and drift apart. Neither had consumers when it was written, which is exactly why its
// tests enumerate every combination of the two states rather than the interesting ones: a rule with no
// callers is trivially correct, and only exhaustive assertions stand in for the real callers.
//
//
// THE THREE EDITING GATES
//
// EnsureEditable is the strict gate: editing is allowed while the company is filling in its profile, or
// when a reviewer has asked for something, and not once it is approved.
//
// EnsureContactDetailsEditable is the looser gate, for how to reach the company. Every child collection
// used to share the strict gate, and that gate stops at approval, so an approved supplier could not
// change a contact, a representative, an address or a branch. People leave and offices move. A buyer
// whose only named contact has left the company cannot ask a question, and the supplier had no way to
// fix it. That was never a rule anybody wrote; it was the registration gate applied to data
// registration was never about.
//
// What stays behind the strict gate is deliberate. Legal identity is what the reviewer approved, and
// bank details are where an award gets paid. Changing either after approval is a claim that needs
// checking rather than a correction, so both keep the strict gate until somebody decides what re-review
// such a change should trigger.
//
// EnsureEditableForComplianceField is the third gate, for the fields the ministry has marked as
// compliance-critical: legal identity, bank accounts and category links. Those stay editable after
// approval, because otherwise a legitimately changed bank account could never be updated at all. Editing
// one while approved sends the application back to review rather than silently accepting the change, and
// the lifecycle stays active throughout, so the supplier is not suspended merely for having an edit
// pending. Which fields count is configuration the caller resolves and passes in, because this record
// knows nothing about configuration. When a field's re-trigger is switched off, it behaves like any other
// profile field and is simply blocked once approved.
//
// UpdateLegalInfo, the bank account methods and the category methods return whether the edit re-triggered
// review, by passing on what that gate itself answered. The caller used to work it out afterwards by
// comparing the state before and after, which was correct only by coincidence.
//
//
// THE PROFILE
//
// Register creates a prospective supplier. The legal identifiers are captured generically, with no
// invented Syrian format rules. The legal identity is seeded with the trading name as an initial legal
// name, which the supplier can correct later, and the registration number lives on the legal identity
// rather than being a parameter here.
//
// UpdatedAt is stamped by the persistence layer wherever this record's version is advanced, rather than
// by each of the thirty-two handlers that write a supplier.
//
// It equals CreatedAt on a supplier nobody has edited, rather than being blank. "Never modified" and
// "modified at the moment it was created" are the same fact to a reader deciding whether their copy is
// stale, and a blank would make every consumer write the same fallback. Both come from one reading of the
// clock, not two: a supplier whose update time is a few ticks after its creation time reads as edited
// since creation to anything comparing them.
//
// UpdateCoreProfile takes the description, website, group and currency. Callers pass already-merged
// values: for a partial save the handler resolves each field to either the supplied value or the current
// one, so this method never has to know which were left out. Which individual fields a supplier may touch
// while a reviewer has asked for something is enforced by the handler, which can read the reviewer's open
// request, and is covered by its own tests rather than asserted here. An earlier comment in this file
// claimed an enforcement that did not exist.
//
//
// THE CHILD COLLECTIONS
//
// A new representative is never the primary one by construction; the caller has to set the primary
// explicitly if that is what they want.
//
// Exactly one representative is primary at all times, and that holds continuously rather than only at
// registration. The last remaining representative can never be removed, because there would be nobody
// left to be primary, and removing the primary while others remain promotes the next one automatically.
//
// A branch's address, when given, must be one of this supplier's own addresses, otherwise a branch could
// point at another supplier's address or at nothing.
//
// A bank account arrives already encrypted and masked, because the encryption service is infrastructure
// the domain does not depend on. The first account added is automatically the default, and exactly one
// account is the default whenever any exist. When the account number is not being changed the caller
// passes nothing for it, and only sends new encrypted and masked values when it actually changes.
// SetDefaultBankAccount lets the supplier pick, on top of the automatic first-added and
// promote-on-removal behaviour.
//
// The six collection caps are safety belts rather than business rules, and none of these collections had
// any cap before. All are realistically small by nature, since a company has a handful of
// representatives, addresses, branches and accounts rather than thousands, unlike the review queue's
// genuinely unbounded growth, so real paging was scoped out in favour of these caps plus tests that
// assert them. They are generous on purpose: high enough that no legitimate supplier ever meets one, low
// enough that a bug or an abuse generating rows in a loop fails loudly instead of growing a response
// forever.
//
//
// COMPLETENESS
//
// RequiredProfileFieldCodes is the full checklist GetMissingProfileFields walks: the core profile fields,
// a minimum of one address and one category link, and accepted terms.
//
// The whole list is exposed so that a completeness percentage has a denominator that cannot drift from
// its numerator. Counting what is missing is easy; counting how many there were in total is where a
// second implementation would appear, and the two would disagree the first time a field was added to the
// checklist and not to the count.
//
// Every entry except accepted terms refers to a profile field code directly rather than a string that
// merely happens to match one. This list feeds what the interface shows as missing, and the interface
// compares those strings against the same vocabulary reviewers flag against. That agreement used to be
// maintained by two independent sets of literals happening to match, so renaming a code would have
// silently desynchronised them, with nothing to catch it until a supplier's "missing" indicator stopped
// matching what a reviewer could actually flag. Referring to the constants makes that a compile error
// instead.
//
// Accepted terms stays a plain string on purpose. It is a submit-gate concept rather than something a
// reviewer can flag for correction, so there is no shared vocabulary to refer to.
//
// AcceptTerms records the version and the moment, which is the consent the submit gate looks for.
// Accepting again, after a later version ships, simply overwrites it, because only the latest acceptance
// needs to be current when the supplier submits.
//
// The terms version is a placeholder until the business owns the content and its versioning.
//
//
// REVIEW
//
// Submit refuses if the profile checklist is incomplete, or if any required document type has no
// satisfying uploaded version. The interface cannot get round it. The list of missing document types is
// computed by the handler, which owns the document query this record has no access to.
//
// PickUpForReview is a reviewer taking the application.
//
// AssignReviewer and UnassignReviewer are a reviewer claiming a queue item and letting it go. Claiming is
// manual self-claim rather than round-robin or manager-assigned, chosen as the simplest model that
// satisfies the requirement without inventing a workflow nobody asked for; nothing in the product
// documents specifies one.
//
// Assignment is independent of the onboarding state. A submitted, under-review or info-requested item can
// all be claimed, and claiming does not itself change the state, so the state machine below never touches
// it.
//
// Approve admits the supplier and makes it active, and raises the obligation to sync it to the finance
// system. Its blocking-documents argument is the approval gate.
//
// The product owner's decision stands unchanged: approval does not require every document to be
// individually approved, and a document still waiting on a reviewer must not block.
//
// What that decision never covered was later fixed. It had been implemented as "refused only if a
// document was rejected, failed its scan or expired", which also let missing and unscanned required
// documents through. The decision was about not requiring approval; the implementation was about not
// requiring presence. Those are different claims, and only the first was ever decided.
//
// Reject needs a reason.
//
// RequestInfo asks the supplier for something. The record of what was asked, the reason and the flagged
// sections and documents, is created by the handler.
//
// Resubmit is the supplier answering. It is an intermediate state that is audited on its own before the
// handler immediately moves the application back to review for the next pass.
//
// Resubmit is gated exactly as Submit is. It used to take no argument at all, which made it a second
// entrance to review with no gate on it. Uploads are permitted while information has been requested and
// versioning only ever adds, so a supplier could re-upload a required document, superseding the approved
// version and leaving the latest one unscanned, come back through this ungated path, and be approved
// holding a required document nobody had looked at. Two entrances to the same state with different guards
// is where the next defect hides, so the argument is required rather than optional: a caller cannot
// forget it, because it will not compile.
//
// Unlike Submit, Resubmit is scoped to what the open request actually asked for rather than to every
// required item. It only ever runs from the info-requested state, so that scoping is safe, and it closes
// a real deadlock rather than a hypothetical one.
//
// The deadlock happened. A reviewer independently rejected one document through the separate
// per-document decision, without also flagging it in the information request. The old unscoped check
// demanded that document be fixed too, but re-uploading it was refused because it was not flagged, and a
// second information request to flag it was refused because the application was not back under review
// yet. The one door out of the info-requested state required walking through a door locked from the far
// side. Scoping to what was actually flagged closes that loop: an unrelated rejected document no longer
// blocks resolving what the ministry actually asked for. If the ministry wants that document fixed too,
// the fix is to flag it, not for the completeness gate to demand it unconditionally.
//
//
// AFTER APPROVAL
//
// Suspend blocks participation, is reversible, and needs a reason. It is not a data change: the supplier
// keeps its profile, its documents and its history, and the requirement to retain historical records
// applies from there on. What it loses is eligibility, which is answered in one place.
//
// Reinstate is the reverse, and it needs a reason too, so the record says why participation was restored
// and not only why it was removed.
//
// Deactivate is final. There is deliberately no way out, not even back to suspended.
//
// It is reachable only from suspended, so deactivation is always a two-step decision. A direct path from
// active would make an irreversible action a single click on a live supplier; requiring suspension first
// means participation has already stopped and somebody has already written down why.
//
// The rule also requires the supplier's users to lose access, and that is not done here: revoking
// sign-ins and killing refresh-token families is an identity concern this record has no reach into. It
// belongs to the handler, and the tests assert it end to end rather than trusting a state field to imply
// it. A deactivated supplier whose users can still refresh their way to a valid session is the same class
// of defect as a second factor that never challenges anybody.
//
//
// THE FINANCE SYSTEM
//
// ExternalId, SyncStatus and LastSyncedAt are written only by the sync path, once a real integration
// exists, and are never settable through an API endpoint.

namespace MotsSupplierPortal.Domain.Suppliers;

using MotsSupplierPortal.Domain.Common;

public enum SupplierSyncStatus
{
    Pending,
    Synced,
    Failed,
}

public sealed class Supplier : IVersionedAggregate, ILastModified
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

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public Guid? AssignedReviewerId { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }

    public const string CurrentTermsVersion = "1.0";

    public IReadOnlyList<Representative> Representatives => _representatives;
    public IReadOnlyList<Address> Addresses => _addresses;
    public IReadOnlyList<Contact> Contacts => _contacts;
    public IReadOnlyList<Branch> Branches => _branches;
    public IReadOnlyList<BankAccount> BankAccounts => _bankAccounts;
    public IReadOnlyList<CategoryLink> CategoryLinks => _categoryLinks;

    private Supplier() { }

    public static Supplier Register(
        string referenceCode,
        string displayNameAr,
        string displayNameEn,
        string? registrationNumber,
        string primaryRepresentativeName,
        string primaryRepresentativeEmail,
        string? primaryRepresentativePhone = null)
    {
        var now = DateTimeOffset.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            DisplayNameAr = displayNameAr,
            DisplayNameEn = displayNameEn,
            OnboardingState = SupplierOnboardingState.Draft,
            CreatedAt = now,
            UpdatedAt = now,
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

    private void EnsureContactDetailsEditable()
    {
        if (OnboardingState is SupplierOnboardingState.Draft or SupplierOnboardingState.Submitted
            or SupplierOnboardingState.UnderReview or SupplierOnboardingState.Rejected)
        {
            throw new DomainException(
                $"Cannot edit contact details from state '{OnboardingState}'.");
        }
    }

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

    public void UpdateCoreProfile(string? description, string? website, string? supplierGroup, string? currencyCode)
    {
        EnsureContactDetailsEditable();
        Description = description;
        Website = website;
        SupplierGroup = supplierGroup;
        CurrencyCode = currencyCode;
        AdvancePastEmailVerified();
    }

    public void SetLogo(string storageKey)
    {
        EnsureContactDetailsEditable();
        LogoStorageKey = storageKey;
    }

    public bool UpdateLegalInfo(string legalNameAr, string legalNameEn, string? registrationNumber, string? taxId, SupplierLegalType supplierType, DateOnly? establishedOn, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        LegalInfo = Domain.Suppliers.LegalInfo.Create(legalNameAr, legalNameEn, registrationNumber, taxId, supplierType, establishedOn);
        AdvancePastEmailVerified();
        return reTriggered;
    }

    private const int MaxRepresentatives = 20;
    private const int MaxAddresses = 20;
    private const int MaxContacts = 20;
    private const int MaxBranches = 50;
    private const int MaxBankAccounts = 10;
    private const int MaxCategoryLinks = 50;

    public Representative AddRepresentative(string fullName, string email, string? phone, string? position)
    {
        EnsureContactDetailsEditable();
        if (_representatives.Count >= MaxRepresentatives)
        {
            throw new DomainException($"A supplier may have at most {MaxRepresentatives} representatives.");
        }
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
        EnsureContactDetailsEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        representative.FullName = fullName;
        representative.Email = email;
        representative.Phone = phone;
        representative.Position = position;
    }

    public void RemoveRepresentative(Guid representativeId)
    {
        EnsureContactDetailsEditable();
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

    public void SetPrimaryRepresentative(Guid representativeId)
    {
        EnsureContactDetailsEditable();
        var representative = _representatives.FirstOrDefault(r => r.Id == representativeId) ?? throw new DomainException("Representative not found.");
        foreach (var r in _representatives) r.IsPrimary = false;
        representative.IsPrimary = true;
    }

    public Address AddAddress(AddressKind kind, string line1, string? line2, string city, string regionCode, string country, string? postalCode, double? latitude, double? longitude)
    {
        EnsureContactDetailsEditable();
        if (_addresses.Count >= MaxAddresses)
        {
            throw new DomainException($"A supplier may have at most {MaxAddresses} addresses.");
        }
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
        EnsureContactDetailsEditable();
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
        EnsureContactDetailsEditable();
        var address = _addresses.FirstOrDefault(a => a.Id == addressId) ?? throw new DomainException("Address not found.");
        _addresses.Remove(address);
        if (address.IsPrimary && _addresses.Count > 0)
        {
            _addresses[0].IsPrimary = true;
        }
    }

    public Contact AddContact(string fullName, string email, string? phone, string? role)
    {
        EnsureContactDetailsEditable();
        if (_contacts.Count >= MaxContacts)
        {
            throw new DomainException($"A supplier may have at most {MaxContacts} contacts.");
        }
        var contact = new Contact { Id = Guid.CreateVersion7(), SupplierId = Id, FullName = fullName, Email = email, Phone = phone, Role = role };
        _contacts.Add(contact);
        return contact;
    }

    public void UpdateContact(Guid contactId, string fullName, string email, string? phone, string? role)
    {
        EnsureContactDetailsEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        contact.FullName = fullName;
        contact.Email = email;
        contact.Phone = phone;
        contact.Role = role;
    }

    public void RemoveContact(Guid contactId)
    {
        EnsureContactDetailsEditable();
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId) ?? throw new DomainException("Contact not found.");
        _contacts.Remove(contact);
    }

    private void EnsureAddressBelongsToThisSupplier(Guid? addressId)
    {
        if (addressId is not null && !_addresses.Any(a => a.Id == addressId))
        {
            throw new DomainException("AddressId does not belong to this supplier.");
        }
    }

    public Branch AddBranch(string nameAr, string nameEn, Guid? addressId)
    {
        EnsureContactDetailsEditable();
        if (_branches.Count >= MaxBranches)
        {
            throw new DomainException($"A supplier may have at most {MaxBranches} branches.");
        }
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = new Branch { Id = Guid.CreateVersion7(), SupplierId = Id, NameAr = nameAr, NameEn = nameEn, AddressId = addressId };
        _branches.Add(branch);
        return branch;
    }

    public void UpdateBranch(Guid branchId, string nameAr, string nameEn, Guid? addressId, bool isActive)
    {
        EnsureContactDetailsEditable();
        EnsureAddressBelongsToThisSupplier(addressId);
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        branch.NameAr = nameAr;
        branch.NameEn = nameEn;
        branch.AddressId = addressId;
        branch.IsActive = isActive;
    }

    public void RemoveBranch(Guid branchId)
    {
        EnsureContactDetailsEditable();
        var branch = _branches.FirstOrDefault(b => b.Id == branchId) ?? throw new DomainException("Branch not found.");
        _branches.Remove(branch);
    }

    public (BankAccount Account, bool ReTriggered) AddBankAccount(string accountHolderName, string bankName, string? branchName, byte[] encryptedAccountNumber, string maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        if (_bankAccounts.Count >= MaxBankAccounts)
        {
            throw new DomainException($"A supplier may have at most {MaxBankAccounts} bank accounts.");
        }
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
        return (account, reTriggered);
    }

    public bool UpdateBankAccount(Guid bankAccountId, string accountHolderName, string bankName, string? branchName, byte[]? encryptedAccountNumber, string? maskedAccountNumber, string? swiftBic, string currencyCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
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
        return reTriggered;
    }

    public bool RemoveBankAccount(Guid bankAccountId, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        _bankAccounts.Remove(account);
        if (account.IsDefault && _bankAccounts.Count > 0)
        {
            _bankAccounts[0].IsDefault = true;
        }
        return reTriggered;
    }

    public void SetDefaultBankAccount(Guid bankAccountId)
    {
        EnsureEditable();
        var account = _bankAccounts.FirstOrDefault(b => b.Id == bankAccountId) ?? throw new DomainException("Bank account not found.");
        foreach (var b in _bankAccounts) b.IsDefault = false;
        account.IsDefault = true;
    }

    public (CategoryLink? Link, bool ReTriggered) LinkCategory(string categoryCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        if (_categoryLinks.Any(l => l.CategoryCode == categoryCode)) return (null, reTriggered);
        if (_categoryLinks.Count >= MaxCategoryLinks)
        {
            throw new DomainException($"A supplier may link at most {MaxCategoryLinks} categories.");
        }
        var link = new CategoryLink { Id = Guid.CreateVersion7(), SupplierId = Id, CategoryCode = categoryCode };
        _categoryLinks.Add(link);
        return (link, reTriggered);
    }

    public bool UnlinkCategory(string categoryCode, bool isComplianceCritical)
    {
        var reTriggered = EnsureEditableForComplianceField(isComplianceCritical);
        var link = _categoryLinks.FirstOrDefault(l => l.CategoryCode == categoryCode);
        if (link is not null) _categoryLinks.Remove(link);
        return reTriggered;
    }

    public static readonly IReadOnlyList<string> RequiredProfileFieldCodes =
    [
        ProfileFieldCodes.LegalInfo,
        ProfileFieldCodes.CurrencyCode,
        ProfileFieldCodes.Address,
        ProfileFieldCodes.CategoryLink,
        ProfileFieldCodes.PrimaryContactPhone,
        "termsAccepted",
    ];

    public IReadOnlyList<string> GetMissingProfileFields()
    {
        var missing = new List<string>();
        if (LegalInfo is null || string.IsNullOrWhiteSpace(LegalInfo.LegalNameAr) || string.IsNullOrWhiteSpace(LegalInfo.LegalNameEn))
        {
            missing.Add(ProfileFieldCodes.LegalInfo);
        }
        if (string.IsNullOrWhiteSpace(CurrencyCode)) missing.Add(ProfileFieldCodes.CurrencyCode);
        if (!_addresses.Any(a => a.Kind == AddressKind.HeadOffice)) missing.Add(ProfileFieldCodes.Address);
        if (_categoryLinks.Count == 0) missing.Add(ProfileFieldCodes.CategoryLink);
        if (_representatives.Any(r => r.IsPrimary && string.IsNullOrWhiteSpace(r.Phone)) || _representatives.All(r => !r.IsPrimary))
        {
            missing.Add(ProfileFieldCodes.PrimaryContactPhone);
        }
        if (TermsAcceptedAt is null) missing.Add("termsAccepted");
        return missing;
    }

    public void AcceptTerms(string version)
    {
        if (OnboardingState is SupplierOnboardingState.Draft)
        {
            throw new DomainException("Cannot accept terms before the email is verified.");
        }

        TermsAcceptedVersion = version;
        TermsAcceptedAt = DateTimeOffset.UtcNow;
    }

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

    public void PickUpForReview()
    {
        if (OnboardingState is not (SupplierOnboardingState.Submitted or SupplierOnboardingState.Resubmitted))
        {
            throw new DomainException(
                $"Cannot pick up for review from state '{OnboardingState}'; only 'Submitted' or 'Resubmitted' is valid.");
        }

        OnboardingState = SupplierOnboardingState.UnderReview;
    }

    public void AssignReviewer(Guid reviewerId)
    {
        AssignedReviewerId = reviewerId;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public void UnassignReviewer()
    {
        AssignedReviewerId = null;
        AssignedAt = null;
    }

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

    public void Suspend(string reason)
    {
        if (LifecycleState != SupplierLifecycleState.Active)
        {
            throw new DomainException(
                $"Cannot suspend from lifecycle state '{LifecycleState}'; only 'Active' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A suspension reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Suspended;
    }

    public void Reactivate(string reason)
    {
        if (LifecycleState != SupplierLifecycleState.Suspended)
        {
            throw new DomainException(
                $"Cannot reactivate from lifecycle state '{LifecycleState}'; only 'Suspended' is valid.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reactivation reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Active;
    }

    public void Deactivate(string reason)
    {
        if (LifecycleState != SupplierLifecycleState.Suspended)
        {
            throw new DomainException(
                $"Cannot deactivate from lifecycle state '{LifecycleState}'; only 'Suspended' is valid. " +
                "Deactivation is terminal and is reachable only via suspension.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A deactivation reason is required.");
        }

        LifecycleState = SupplierLifecycleState.Deactivated;
    }

    public bool IsEligibleToParticipate =>
        OnboardingState == SupplierOnboardingState.Approved
        && LifecycleState == SupplierLifecycleState.Active;

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

    public void RequestInfo()
    {
        if (OnboardingState != SupplierOnboardingState.UnderReview)
        {
            throw new DomainException(
                $"Cannot request info from state '{OnboardingState}'; only 'UnderReview' is valid.");
        }

        OnboardingState = SupplierOnboardingState.InfoRequested;
    }

    public void Resubmit(
        IReadOnlyList<string> missingRequiredDocumentTypeCodes,
        IReadOnlyList<string> flaggedProfileFields,
        IReadOnlyList<string> flaggedDocumentTypeCodes)
    {
        if (OnboardingState != SupplierOnboardingState.InfoRequested)
        {
            throw new DomainException(
                $"Cannot resubmit from state '{OnboardingState}'; only 'InfoRequested' is valid.");
        }

        var missing = GetMissingProfileFields().Where(flaggedProfileFields.Contains)
            .Concat(missingRequiredDocumentTypeCodes.Where(flaggedDocumentTypeCodes.Contains))
            .ToList();
        if (missing.Count > 0)
        {
            throw new DomainException($"Cannot resubmit: missing required items: {string.Join(", ", missing)}.");
        }

        OnboardingState = SupplierOnboardingState.Resubmitted;
    }

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
