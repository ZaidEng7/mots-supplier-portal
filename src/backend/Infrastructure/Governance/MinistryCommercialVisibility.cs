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

namespace MotsSupplierPortal.Infrastructure.Governance;

internal static class MinistryCommercialVisibility
{
    public static Task<bool> IsOnAsync(AppDbContext db, CancellationToken ct) =>
        SupplierFieldConfigLookup.IsEnabledAsync(
            db, FieldConfigCategory.GovernanceVisibility, "commercialValues", defaultValue: false, ct);

    /// <summary>Bid totals for the named proposals, summed from their lines.
    ///
    /// <para>Summed in memory over at most a page's worth of proposals, for the same reason
    /// <c>GetGovernanceOverviewHandler</c> gives: the SQL shape that groups across two DbSets did not
    /// translate and answered 500 once the dataset grew.</para></summary>
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
