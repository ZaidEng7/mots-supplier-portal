// Builds the procurement report: tender volume, cycle time and award outcomes, scoped to the caller's own
// organization in the query like every other cross-record read.
//
//
// A COUNT LEAKS EVEN WHEN NO ROW IS SHOWN
//
// A figure saying forty-one tenders were published, which includes another organization's rows, discloses
// volume without disclosing a row. No list-level test catches it, because no list is involved.
//
// The organization condition is the first clause of every query here, and the tests assert the numbers
// rather than the shape of the response.
//
// The cycle-time intervals are each defined as the pair of recorded actions that bound them, so an
// interval exists only where both ends are actually recorded.

namespace MotsSupplierPortal.Infrastructure.Reports;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reports;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ProcurementReportHandler(AppDbContext db, IScopeContext scope) : IProcurementReportHandler
{
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
        if (scope.OrganizationId is not { } organizationId) return null;

        var rfqs = db.Rfqs.AsNoTracking().Where(r => r.OrganizationId == organizationId);

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
                .Where(hours => hours >= 0)
                .OrderBy(hours => hours)
                .ToList();

            return new CycleTimeIntervalDto(interval.Key, durations.Count, Median(durations));
        }).ToList();

        var coverageFloor = transitions.Count == 0 ? (DateTimeOffset?)null : transitions.Min(t => t.OccurredAt);

        return new ProcurementReportDto(
            byState.Select(b => new ReportCountDto(b.State.ToString(), b.Count)).OrderBy(c => c.Key).ToList(),
            cycleTimes,
            awardsByState.Select(a => new ReportCountDto(a.State.ToString(), a.Count)).OrderBy(c => c.Key).ToList(),
            byState.Sum(b => b.Count),
            coverageFloor);
    }

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
