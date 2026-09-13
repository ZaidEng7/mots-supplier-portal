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

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>Shared loader: every RFQ handler in this file row-scopes to the caller's own
/// OrganizationId (BRULE-029: "An RFQ is created and owned by a procurement_officer and is scoped
/// to their Organization; cross-org authoring is prohibited"). A null scope.OrganizationId (e.g. a
/// supplier-side or platform caller with no org membership) can never see or touch any RFQ - same
/// "no scope, no access" pattern as scope.SupplierId is null on the supplier side.</summary>
internal static class RfqLoader
{
    // AsSplitQuery: four sibling collections in one single JOIN query produces a cartesian-product
    // row multiplication (Items x Requirements x Attachments x Approvals) - not itself the cause
    // of a real concurrency bug found while building this (see SubmitRfqForReviewHandler's own
    // comment for that one), but a real, separate performance concern worth avoiding regardless
    // once four sibling collections are all included together.
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
