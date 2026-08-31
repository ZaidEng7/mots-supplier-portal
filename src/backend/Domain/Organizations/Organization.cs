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

    public static Organization Create(string legalNameAr, string legalNameEn, OrganizationType organizationType, string? contactEmail = null, string? contactPhone = null)
    {
        if (string.IsNullOrWhiteSpace(legalNameAr)) throw new DomainException("Organization legal name (Arabic) is required.");
        if (string.IsNullOrWhiteSpace(legalNameEn)) throw new DomainException("Organization legal name (English) is required.");

        return new Organization
        {
            Id = Guid.CreateVersion7(),
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
