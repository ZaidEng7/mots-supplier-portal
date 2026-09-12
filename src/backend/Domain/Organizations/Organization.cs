using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Domain.Organizations;

public enum OrganizationType
{
    Hotel,
    MotBody,
    Ministry,
}

public enum OrganizationSyncStatus
{
    Pending,
    Synced,
    Failed,
}

/// <summary>
/// A buying entity (docs/architecture/DOMAIN-MODEL.md §5.2): a Hotel, an MOT-affiliated body, or
/// the Ministry itself. Task #7/Stage A: data model only - nothing yet references this aggregate.
/// AppUser.OrganizationId stays a bare claim (Stage B), no endpoint or UI creates/links one
/// (Stage C), and the IdP seam is unrelated (Stage D). Mirrors Supplier's own conventions rather
/// than inventing a new shape: scalar sync markers instead of a separate ExternalSyncInfo type
/// (Supplier itself has no such type either, despite the foundational doc naming one), and reuses
/// Domain.Suppliers.DomainException rather than adding a near-duplicate exception type - it is a
/// generic one-liner with no Supplier-specific state, just homed in the first domain that needed it.
/// </summary>
public sealed class Organization
{
    private readonly List<OrgUnit> _orgUnits = [];

    public Guid Id { get; private init; }

    /// <summary>
    /// T-055/§12.4's <c>buyingOrg.code</c>: the buying body's opaque public identifier,
    /// <c>ORG-2026-000001</c>, allocated by the same counter every other reference code uses.
    ///
    /// <para><b>What was there before.</b> Nothing. §12.4 documents the field; the entry assumed
    /// <c>ExternalId</c> stood in for it, and it cannot - <c>ExternalId</c> has a private setter and
    /// no writer anywhere in the codebase, so the documented field was null in every response the API
    /// could produce. An ERP identifier would have been the wrong answer regardless: it belongs to
    /// another system and is absent until that system says otherwise.</para>
    ///
    /// <para><b>Addressing is unchanged.</b> Routes still take the organization's id where they take
    /// one at all, and this code is what the WIRE carries - the same split §3 draws for suppliers and
    /// tenders. Making it addressable is a separate decision nobody has needed yet.</para>
    /// </summary>
    public string ReferenceCode { get; private init; } = null!;

    public string LegalNameAr { get; private set; } = null!;
    public string LegalNameEn { get; private set; } = null!;
    public OrganizationType OrganizationType { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ExternalId { get; private set; }
    public OrganizationSyncStatus SyncStatus { get; private set; } = OrganizationSyncStatus.Pending;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private init; }

    public IReadOnlyList<OrgUnit> OrgUnits => _orgUnits;

    /// <summary>DOMAIN-MODEL.md §5.2: OrganizationType = Ministry implies read-only governance
    /// scope for its users (foundational §6). NOT ENFORCED HERE OR ANYWHERE YET - recorded on the
    /// entity now so the invariant is not forgotten by the time Stage B/C wire AppUser.OrganizationId
    /// and permissions actually consult it.</summary>
    public bool IsMinistry => OrganizationType == OrganizationType.Ministry;

    private Organization() { }

    /// <summary>
    /// T-055: <paramref name="referenceCode"/> is allocated by the caller, from the shared counter.
    ///
    /// <para>Passed in rather than generated here for the reason every other aggregate in this
    /// codebase passes it in: the allocator is a database statement and the domain has no database.
    /// A caller that forgets it gets a null-reference at persistence rather than a silent blank, and
    /// there is exactly one caller.</para>
    /// </summary>
    public static Organization Create(string referenceCode, string legalNameAr, string legalNameEn, OrganizationType organizationType, string? contactEmail = null, string? contactPhone = null)
    {
        if (string.IsNullOrWhiteSpace(referenceCode)) throw new DomainException("Organization reference code is required.");
        if (string.IsNullOrWhiteSpace(legalNameAr)) throw new DomainException("Organization legal name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(legalNameEn)) throw new DomainException("Organization legal name (English) is required.");

        return new Organization
        {
            Id = Guid.CreateVersion7(),
            ReferenceCode = referenceCode,
            LegalNameAr = legalNameAr,
            LegalNameEn = legalNameEn,
            OrganizationType = organizationType,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Self-nesting per §5.2: an OrgUnit tree has no cycles and every OrgUnit belongs to
    /// exactly one Organization. Cycle-freedom is trivially guaranteed here - a new unit can only
    /// ever reference an ALREADY-existing unit in this same Organization as its parent, so no edge
    /// can point forward into a unit that doesn't exist yet.</summary>
    public OrgUnit AddOrgUnit(string name, Guid? parentOrgUnitId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("OrgUnit name is required.");
        if (parentOrgUnitId is not null && _orgUnits.All(u => u.Id != parentOrgUnitId))
        {
            throw new DomainException("Parent OrgUnit must belong to this Organization.");
        }

        var unit = new OrgUnit
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Id,
            ParentOrgUnitId = parentOrgUnitId,
            Name = name,
        };
        _orgUnits.Add(unit);
        return unit;
    }
}
