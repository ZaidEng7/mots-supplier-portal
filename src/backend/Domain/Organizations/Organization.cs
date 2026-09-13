// A buying body: a hotel, a transport body affiliated to the ministry, or the ministry
// itself. Tenders belong to one, and staff accounts belong to one.
//
// ReferenceCode is the public identifier, ORG-2026-000001, allocated from the same counter
// that numbers tenders, suppliers and bids. Routes still address an organization by its
// internal id; the reference code is what the wire carries and what a person quotes. The
// code is passed into Create rather than generated here, because allocating it is a
// database statement and the domain has no database.
//
// IsMinistry marks the governance role: a ministry user sees across every buying body and
// changes nothing. That is recorded here so the rule is not forgotten, but it is enforced
// by the permission set rather than by this property.
//
// The ERP fields - ExternalId, SyncStatus, LastSyncedAt - are the seam to the ministry's
// finance system. They follow the same shape a supplier uses rather than introducing a new
// one.
//
// OrgUnits is the department tree. Units are added through AddOrgUnit only, which is what
// keeps the tree free of cycles: a new unit can only name a parent that already exists.

namespace MotsSupplierPortal.Domain.Organizations;

using MotsSupplierPortal.Domain.Suppliers;

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

public sealed class Organization
{
    private readonly List<OrgUnit> _orgUnits = [];

    public Guid Id { get; private init; }
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

    public bool IsMinistry => OrganizationType == OrganizationType.Ministry;

    private Organization() { }

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
