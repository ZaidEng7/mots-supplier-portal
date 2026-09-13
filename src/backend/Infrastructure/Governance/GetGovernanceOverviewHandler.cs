// The ministry's overview of the whole system: suppliers, tenders, awards and participation.
//
//
// NO ORGANIZATION FILTER ANYWHERE, AND THAT IS THE POINT
//
// The written rule grants the ministry cross-organization access, so these queries deliberately do not scope to
// the caller's buying body, unlike every other read in this codebase.
//
// That inversion is why the permission is its own rather than a borrowed tender-read: a route that skips
// row-scoping must be reachable only by a persona whose whole purpose is to skip it.
//
//
// AGGREGATES ONLY
//
// Every value returned is a count or an average. No supplier name, no tender code, no actor, so there is no
// per-row filter a later edit could forget, which is the failure the rule exists to prevent.
//
//
// "AWARDS" MEANS AWARDED
//
// An award row exists from the moment a manager RECOMMENDS one, and stays through approval and rejection, none of
// which is an award to the reader of a governance dashboard.
//
// Counting every row put one number under "awards" beside a total value computed from the ones actually awarded,
// and the screen the tile drills into showed the second number. Three figures, one word, two meanings.
//
//
// PARTICIPATION COUNTS ONLY TENDERS THAT REACHED THE MARKET
//
// Published or later, because a draft tender has had no chance to attract a bid and including it would drag the
// average toward zero for a reason that is not about participation at all.
//
// And "not a draft bid" stopped meaning "was submitted" once the bid states widened. A lapsed draft was never
// submitted, so counting it would overstate the figure. A cancelled bid stays counted: that bid WAS submitted, and
// the tender being withdrawn afterwards does not unmake the supplier's participation.
//
//
// MONEY IS BEHIND A SWITCH, AND IS ONLY COMPUTED WHEN IT MAY BE SHOWN
//
// The switch reuses the administrator-editable table rather than a second settings mechanism, so the ministry's
// legal answer is a row edit. It defaults to off when the row is missing, because a lookup that failed open would
// disclose exactly what the default exists to withhold.
//
// Not computed and then hidden: a value that exists in memory behind a flag is one refactor away from being
// serialised by accident.
//
// It is two steps rather than one query across two tables. That form does not translate and answered with a server
// error on every request with the flag on: the winning identifiers first, then the totals over them.
//
// The totals are summed in memory. A database sum failed once the dataset grew past a handful of awards, and the
// shape that failed is not worth debugging for a figure computed from at most a few hundred rows.

namespace MotsSupplierPortal.Infrastructure.Governance;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Governance;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

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

        var totalAwards = await db.Awards.AsNoTracking().CountAsync(a => a.State == AwardState.Awarded, ct);

        var publishedRfqs = await db.Rfqs.AsNoTracking()
            .CountAsync(r => r.State != RfqState.Draft && r.State != RfqState.InternalReview
                             && r.State != RfqState.Approved && r.State != RfqState.Cancelled, ct);
        var proposals = await db.Proposals.AsNoTracking()
            .CountAsync(p => p.State != ProposalState.Draft && p.State != ProposalState.Lapsed, ct);

        var averageProposals = publishedRfqs == 0
            ? 0m
            : Math.Round((decimal)proposals / publishedRfqs, 1);

        var commercialVisible = await SupplierFieldConfigLookup.IsEnabledAsync(
            db, FieldConfigCategory.GovernanceVisibility, "commercialValues", defaultValue: false, ct);

        decimal? awardedValue = null;
        if (commercialVisible)
        {
            var winningProposalIds = await db.Awards.AsNoTracking()
                .Where(a => a.State == AwardState.Awarded)
                .Select(a => a.WinningProposalId)
                .ToListAsync(ct);

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
