// The shared load every buyer-side tender handler starts from.
//
// It scopes to the caller's own organization, because the written rule says a tender is owned by the officer
// who created it and scoped to their organization, and authoring across organizations is prohibited.
//
// A caller with no organization, a supplier or a platform administrator, can never see or touch any tender.
// That is the same no-scope-no-access shape the supplier side has.
//
// The seven child collections are fetched as separate statements rather than one join. Four sibling
// collections in a single join multiply into a product of rows. That was not the cause of a real concurrency
// defect found while building this, which the submit handler's own header explains, but it is a real and
// separate performance concern worth avoiding once this many collections are included together.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

internal static class RfqLoader
{
    public static IQueryable<Rfq> IncludeAll(this DbSet<Rfq> set) =>
        set.Include(r => r.Items).Include(r => r.Requirements).Include(r => r.Attachments).Include(r => r.Approvals)
            .Include(r => r.Invitations).Include(r => r.Clarifications).Include(r => r.Addenda)
            .AsSplitQuery();

    public static async Task<Rfq?> LoadScopedAsync(AppDbContext db, IScopeContext scope, string referenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        return await db.Rfqs.IncludeAll()
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode && r.OrganizationId == scope.OrganizationId, ct);
    }
}
