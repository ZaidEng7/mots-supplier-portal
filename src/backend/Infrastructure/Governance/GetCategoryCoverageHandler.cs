using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Governance;

/// <summary>
/// SCR-604, under BRULE-086.
///
/// <para><b>No organization predicate, deliberately</b> - the same inversion <c>GetGovernanceOverviewHandler</c>
/// documents. A category's coverage is a national question, and filtering it by the reader's own buying body
/// would answer a different one silently.</para>
///
/// <para><b>Driven by the CATEGORY list, not by the links.</b> Grouping the links would produce a list of
/// categories that have suppliers, which is the opposite of what a coverage screen is for: the empty rows are
/// the finding. Five small aggregate queries, each keyed by category code, then joined in memory over a list
/// that is a few dozen rows long - a single query with five correlated subqueries reads worse and buys
/// nothing at this size.</para>
/// </summary>
public sealed class GetCategoryCoverageHandler(AppDbContext db) : IGetCategoryCoverageHandler
{
    /// <summary>Tender states that mean the market was actually asked. Draft, InternalReview and Approved
    /// never reached a supplier; Cancelled withdrew the question. Both would count a tender that asked this
    /// category for nothing.</summary>
    private static readonly RfqState[] ReachedTheMarket =
    [
        RfqState.Published, RfqState.SubmissionOpen, RfqState.SubmissionClosed, RfqState.UnderEvaluation,
        RfqState.Clarification, RfqState.Shortlisting, RfqState.Recommendation, RfqState.AwardApproval,
        RfqState.Awarded, RfqState.Completed,
    ];

    public async Task<CategoryCoverageOverviewDto> HandleAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.NameEn)
            .Select(c => new { c.Code, c.NameAr, c.NameEn })
            .ToListAsync(ct);

        var approvedByCategory = await db.CategoryLinks.AsNoTracking()
            .Where(l => db.Suppliers.Any(s => s.Id == l.SupplierId
                                              && s.OnboardingState == SupplierOnboardingState.Approved))
            .GroupBy(l => l.CategoryCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);

        // Approved AND Active. The two numbers differ exactly when a supplier has been suspended, and a
        // category whose only supplier is suspended reads as covered until both are on the row.
        var activeByCategory = await db.CategoryLinks.AsNoTracking()
            .Where(l => db.Suppliers.Any(s => s.Id == l.SupplierId
                                              && s.OnboardingState == SupplierOnboardingState.Approved
                                              && s.LifecycleState == SupplierLifecycleState.Active))
            .GroupBy(l => l.CategoryCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);

        var offeringsByCategory = await db.Offerings.AsNoTracking()
            .Where(o => o.IsActive)
            .GroupBy(o => o.CategoryCode)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Code, x => x.Count, ct);

        // DISTINCT tenders per category: an RFQ with four catering lines asked the catering market once.
        var tenderPairs = await db.RfqItems.AsNoTracking()
            .Where(i => db.Rfqs.Any(r => r.Id == i.RfqId && ReachedTheMarket.Contains(r.State)))
            .Select(i => new { i.CategoryCode, i.RfqId })
            .Distinct()
            .ToListAsync(ct);

        var awardedRfqIds = await db.Awards.AsNoTracking()
            .Where(a => a.State == AwardState.Awarded)
            .Select(a => a.RfqId)
            .Distinct()
            .ToListAsync(ct);
        var awardedSet = awardedRfqIds.ToHashSet();

        var tendersByCategory = tenderPairs
            .GroupBy(p => p.CategoryCode)
            .ToDictionary(g => g.Key, g => g.Count());
        var awardedByCategory = tenderPairs
            .Where(p => awardedSet.Contains(p.RfqId))
            .GroupBy(p => p.CategoryCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var rows = categories
            .Select(c => new CategoryCoverageDto(
                c.Code, c.NameAr, c.NameEn,
                approvedByCategory.GetValueOrDefault(c.Code),
                activeByCategory.GetValueOrDefault(c.Code),
                offeringsByCategory.GetValueOrDefault(c.Code),
                tendersByCategory.GetValueOrDefault(c.Code),
                awardedByCategory.GetValueOrDefault(c.Code)))
            .ToList();

        return new CategoryCoverageOverviewDto(
            rows,
            rows.Count(r => r.ActiveSuppliers == 0),
            // MSP-54's list is flat and says so in its own doc comment. Reported rather than presented as a
            // tree, because a hierarchy nobody has decided is not one a screen may invent.
            CategoriesAreFlat: true);
    }
}
