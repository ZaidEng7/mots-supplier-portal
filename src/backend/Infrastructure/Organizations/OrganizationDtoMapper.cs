// Turning a buying body into its read model, for the administration screens.
//
// A buying body has no public reference code and no addresses, and both are real gaps against the written model
// rather than omissions here. They were flagged in the report for the stage that built this rather than silently
// invented, because this builds against what actually shipped.
//
// The internal identifiers are used directly in these routes, which is a deliberate, scoped exception to the
// convention that internal keys are never exposed. This surface is staff-only behind its own permission and is
// never supplier-facing or public.

namespace MotsSupplierPortal.Infrastructure.Organizations;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

internal static class OrganizationDtoMapper
{
    public static OrganizationDto ToDto(Organization org) => new(
        org.Id, org.LegalNameAr, org.LegalNameEn, org.OrganizationType, org.ContactEmail, org.ContactPhone, org.IsActive,
        [.. org.OrgUnits.Select(u => new OrgUnitDto(u.Id, u.OrganizationId, u.ParentOrgUnitId, u.Name))]);
}
