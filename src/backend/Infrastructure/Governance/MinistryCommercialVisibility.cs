// Shared by the ministry's four screens: may the ministry see money, and what is a bid worth.
//
// The switch is read on every request rather than cached. It is a policy switch, and the point of a policy switch
// is that turning it off takes effect now. A cached "yes" would keep disclosing for the lifetime of a process after
// somebody decided to stop.
//
// The totals are summed in memory over at most a page's worth of bids, for the reason the governance overview
// gives: the query shape that groups across two tables did not translate and answered with a server error once the
// dataset grew.

namespace MotsSupplierPortal.Infrastructure.Governance;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Infrastructure.Suppliers;

internal static class MinistryCommercialVisibility
{
    public static Task<bool> IsOnAsync(AppDbContext db, CancellationToken ct) =>
        SupplierFieldConfigLookup.IsEnabledAsync(
            db, FieldConfigCategory.GovernanceVisibility, "commercialValues", defaultValue: false, ct);

    public static async Task<Dictionary<Guid, decimal>> TotalsByProposalAsync(
        AppDbContext db, IReadOnlyCollection<Guid> proposalIds, CancellationToken ct)
    {
        if (proposalIds.Count == 0) return [];

        var lines = await db.ProposalItems.AsNoTracking()
            .Where(i => proposalIds.Contains(i.ProposalId))
            .Select(i => new { i.ProposalId, i.Quantity, i.UnitPrice, i.Discount })
            .ToListAsync(ct);

        return lines
            .GroupBy(l => l.ProposalId)
            .ToDictionary(g => g.Key, g => g.Sum(l => (l.Quantity * l.UnitPrice) - (l.Discount ?? 0m)));
    }
}
