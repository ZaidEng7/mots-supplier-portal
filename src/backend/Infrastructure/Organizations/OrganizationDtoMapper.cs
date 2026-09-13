using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Organizations;

/// <summary>Task #7/Stage C: admin-only surface. Organization has no public ReferenceCode
/// (Stage A did not add one - DOMAIN-MODEL.md §5.2 lists Address[] as an Organization value
/// object too, also not added in Stage A). Both are real gaps against the design doc, flagged
/// in this stage's report rather than silently expanded into here - this stage builds against
/// what Stage A actually shipped. Internal Guid ids are used directly in these admin routes as a
/// deliberate, scoped exception to the "never expose internal PKs" convention (foundational §2):
/// this surface is staff-only (admin.organizations.manage), never supplier- or public-facing.</summary>
internal static class OrganizationDtoMapper
{
    public static OrganizationDto ToDto(Organization org) => new(
        org.Id, org.LegalNameAr, org.LegalNameEn, org.OrganizationType, org.ContactEmail, org.ContactPhone, org.IsActive,
        [.. org.OrgUnits.Select(u => new OrgUnitDto(u.Id, u.OrganizationId, u.ParentOrgUnitId, u.Name))]);
}
