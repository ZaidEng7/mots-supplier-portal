// The shared load every award handler starts from: the tender within the caller's organization, and its award if
// it has one.
//
// It returns the tender even when there is no award yet, because recommending is what creates one.

namespace MotsSupplierPortal.Infrastructure.Awards;

using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class AwardLoader
{
    public static IQueryable<Award> IncludeAll(this DbSet<Award> set) => set.Include(a => a.Approvals);

    public static async Task<(Rfq Rfq, Award? Award)?> LoadScopedAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var award = await db.Awards.IncludeAll().FirstOrDefaultAsync(a => a.RfqId == rfq.Id, ct);
        return (rfq, award);
    }
}
