using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Reports;

/// <summary>
/// FEAT-19.1: procurement reporting, org-scoped in the query like every other cross-aggregate read.
///
/// <para><b>A COUNT leaks even when no row is shown.</b> "RFQs published: 41" that includes another
/// organization's rows discloses volume without disclosing a row, and no list-level test catches it,
/// because no list is involved. The organization predicate is the first clause of every query here
/// and the tests assert the numbers rather than the shape.</para>
/// </summary>
public sealed class ProcurementReportHandler(AppDbContext db, IScopeContext scope) : IProcurementReportHandler
{
    /// <summary>
    /// The cycle-time intervals that are actually derivable, each as the pair of audited actions
    /// that bound it.
    ///
    /// <para>EPIC-19 part 2 established what the audit log carries: every RFQ state transition
    /// writes a row with FromState, ToState and OccurredAt, across some thirty audited actions
    /// covering the whole lifecycle, retained indefinitely (ASM-085). So an interval between two
    /// transitions is a query, not an estimate.</para>
    ///
    /// <para><b>What is deliberately NOT here.</b> Time spent in the CURRENT state - how long an RFQ
    /// has been sitting where it is now - is the number a reader most wants and the one this cannot
    /// honestly produce for older rows: it needs the timestamp of the last transition, which exists
    /// only for RFQs whose transition was audited. Approximating it from <c>created_at</c> would
    /// report "waiting 400 days" for an RFQ that moved yesterday. Nothing here substitutes a proxy
    /// for a measurement.</para>
    /// </summary>
    private static readonly (string Key, string From, string To)[] Intervals =
    [
        ("DraftToReview", "rfq_created", "rfq_submitted_for_review"),
        ("ReviewToApproved", "rfq_submitted_for_review", "rfq_approved"),
        ("ApprovedToPublished", "rfq_approved", "rfq_published"),
        ("PublishedToSubmissionClosed", "rfq_published", "rfq_submission_closed"),
        ("SubmissionClosedToEvaluation", "rfq_submission_closed", "rfq_evaluation_opened"),
        ("EvaluationToAward", "rfq_evaluation_opened", "rfq_awarded"),
    ];

    public async Task<ProcurementReportDto?> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        // §9.2: no organization, no report - a 404 rather than an empty one, which would assert the
        // organization exists and has done nothing.
        if (scope.OrganizationId is not { } organizationId) return null;

        var rfqs = db.Rfqs.AsNoTracking().Where(r => r.OrganizationId == organizationId);

        // Same period semantics as SCR-400: filtered on PublishedAt, and an RFQ never published is
        // always included, because excluding it would empty the pre-publication states whenever a
        // period was chosen.
        if (from is { } start) rfqs = rfqs.Where(r => r.PublishedAt == null || r.PublishedAt >= start);
        if (to is { } end) rfqs = rfqs.Where(r => r.PublishedAt == null || r.PublishedAt <= end);

        var byState = await rfqs
            .GroupBy(r => r.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var awardsByState = await db.Awards.AsNoTracking()
            .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.OrganizationId == organizationId))
            .GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var rfqIds = await rfqs.Select(r => r.Id).ToListAsync(ct);

        // Only the actions the intervals need, and only for this organization's RFQs. Pulled once
        // and paired in memory: the pairing is per-RFQ and the medians are over the paired set, and
        // expressing that as SQL would need a self-join per interval for no benefit at report scale.
        var actions = Intervals.SelectMany(i => new[] { i.From, i.To }).Distinct().ToArray();

        var transitions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateType == "Rfq" && rfqIds.Contains(a.AggregateId) && actions.Contains(a.Action))
            .Select(a => new { a.AggregateId, a.Action, a.OccurredAt })
            .ToListAsync(ct);

        var firstOccurrence = transitions
            .GroupBy(t => (t.AggregateId, t.Action))
            .ToDictionary(g => g.Key, g => g.Min(t => t.OccurredAt));

        var cycleTimes = Intervals.Select(interval =>
        {
            var durations = rfqIds
                .Where(id => firstOccurrence.ContainsKey((id, interval.From))
                          && firstOccurrence.ContainsKey((id, interval.To)))
                .Select(id => (firstOccurrence[(id, interval.To)] - firstOccurrence[(id, interval.From)]).TotalHours)
                // A negative duration would mean the pair was recorded out of order - a returned RFQ
                // re-entering review, for instance. Excluded rather than folded into a median it
                // would drag below zero, where it would read as an impossibly fast process.
                .Where(hours => hours >= 0)
                .OrderBy(hours => hours)
                .ToList();

            return new CycleTimeIntervalDto(interval.Key, durations.Count, Median(durations));
        }).ToList();

        // The earliest audited transition on this organization's RFQs: the floor below which no
        // cycle-time figure here can see. See ProcurementReportDto.CoverageFloor for why it travels
        // in the payload rather than as a footnote.
        var coverageFloor = transitions.Count == 0 ? (DateTimeOffset?)null : transitions.Min(t => t.OccurredAt);

        return new ProcurementReportDto(
            byState.Select(b => new ReportCountDto(b.State.ToString(), b.Count)).OrderBy(c => c.Key).ToList(),
            cycleTimes,
            awardsByState.Select(a => new ReportCountDto(a.State.ToString(), a.Count)).OrderBy(c => c.Key).ToList(),
            byState.Sum(b => b.Count),
            coverageFloor);
    }

    /// <summary>Median of a pre-sorted list; null when there is nothing to take a median of.</summary>
    private static decimal? Median(List<double> sorted)
    {
        if (sorted.Count == 0) return null;

        var middle = sorted.Count / 2;
        var value = sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;

        return Math.Round((decimal)value, 1);
    }
}
