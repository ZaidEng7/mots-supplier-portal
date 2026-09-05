using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Governance;

/// <summary>
/// FR-DSH-005/SCR-600, under D-6.
///
/// <para><b>No organization predicate anywhere, and that is the point.</b> BRULE-086 grants the
/// Ministry cross-organization access, so these queries deliberately do NOT filter by
/// <c>scope.OrganizationId</c> - unlike every other read in this codebase. That inversion is why the
/// permission is its own (<c>governance.read</c>) rather than a borrowed <c>rfq.read</c>: a route that
/// skips row-scoping must be reachable only by a persona whose whole purpose is to skip it.</para>
///
/// <para><b>Aggregates only.</b> Every value returned is a count or an average. No supplier name, no
/// RFQ code, no actor - so there is no per-row filter that a later edit could forget, which is the
/// failure mode BRULE-086 exists to prevent.</para>
/// </summary>
public sealed class GetGovernanceOverviewHandler(AppDbContext db) : IGetGovernanceOverviewHandler
{
    public async Task<GovernanceOverviewDto> HandleAsync(CancellationToken ct)
    {
        var suppliersByState = await db.Suppliers.AsNoTracking()
            .GroupBy(s => s.LifecycleState)
            .Select(g => new GovernanceCountDto(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        var rfqsByState = await db.Rfqs.AsNoTracking()
            .GroupBy(r => r.State)
            .Select(g => new GovernanceCountDto(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        var totalAwards = await db.Awards.AsNoTracking().CountAsync(ct);

        // Participation: proposals per RFQ that actually reached the market. Published-or-later, because
        // a Draft RFQ has had no chance to attract one and including it would drag the average toward
        // zero for a reason that is not about participation at all.
        var publishedRfqs = await db.Rfqs.AsNoTracking()
            .CountAsync(r => r.State != RfqState.Draft && r.State != RfqState.InternalReview
                             && r.State != RfqState.Approved && r.State != RfqState.Cancelled, ct);
        // A-9 widened ProposalState, and "not Draft" stopped meaning "was submitted": a LAPSED draft was
        // never submitted at all, so counting it here would overstate participation in the governance
        // figure the Ministry reads. Cancelled stays counted - that bid WAS submitted, and the tender
        // being withdrawn afterwards does not unmake the supplier's participation.
        var proposals = await db.Proposals.AsNoTracking()
            .CountAsync(p => p.State != ProposalState.Draft && p.State != ProposalState.Lapsed, ct);

        var averageProposals = publishedRfqs == 0
            ? 0m
            : Math.Round((decimal)proposals / publishedRfqs, 1);

        // D-6/BRULE-087. Reuses the admin-editable config table rather than a second settings
        // mechanism, so MOT Legal's answer is a row edit. Defaults to FALSE if the row is missing
        // entirely - a lookup that fails open would disclose exactly what the default exists to
        // withhold.
        var commercialVisible = await SupplierFieldConfigLookup.IsEnabledAsync(
            db, FieldConfigCategory.GovernanceVisibility, "commercialValues", defaultValue: false, ct);

        decimal? awardedValue = null;
        if (commercialVisible)
        {
            // Only computed when it may be shown. Not computed-then-hidden: a value that exists in
            // memory behind a flag is one refactor away from being serialized by accident.
            // Two steps, not a SelectMany over a second DbSet: that form does not translate and
            // answered 500 on every request with the flag on. The winning ids first, then the sum over
            // them - both plain SQL.
            var winningProposalIds = await db.Awards.AsNoTracking()
                .Where(a => a.State == AwardState.Awarded)
                .Select(a => a.WinningProposalId)
                .ToListAsync(ct);

            // Summed in memory over the winning bids' lines. A SQL SUM here answered 500 once the
            // dataset grew past a handful of awards, and the shape that failed is not worth debugging
            // for a figure computed from at most a few hundred rows - the same call this handler's
            // list queries already make.
            var lineTotals = winningProposalIds.Count == 0
                ? []
                : await db.ProposalItems.AsNoTracking()
                    .Where(i => winningProposalIds.Contains(i.ProposalId))
                    .Select(i => i.LineTotal)
                    .ToListAsync(ct);

            awardedValue = lineTotals.Sum();
        }

        return new GovernanceOverviewDto(
            suppliersByState.Sum(c => c.Count),
            [.. suppliersByState.OrderBy(c => c.Key, StringComparer.Ordinal)],
            rfqsByState.Sum(c => c.Count),
            [.. rfqsByState.OrderBy(c => c.Key, StringComparer.Ordinal)],
            totalAwards,
            averageProposals,
            awardedValue,
            commercialVisible);
    }
}
