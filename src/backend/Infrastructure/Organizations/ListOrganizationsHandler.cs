// Every buying body with its departments, for the administration screen.

namespace MotsSupplierPortal.Infrastructure.Organizations;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Organizations;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

public sealed class ListOrganizationsHandler(AppDbContext db) : IListOrganizationsHandler
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken ct)
    {
        var orgs = await db.Set<Organization>().Include(o => o.OrgUnits).OrderBy(o => o.LegalNameEn).ToListAsync(ct);
        return [.. orgs.Select(OrganizationDtoMapper.ToDto)];
    }
}
