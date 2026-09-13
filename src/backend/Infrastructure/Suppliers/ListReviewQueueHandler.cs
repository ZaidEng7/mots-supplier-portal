using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class ListReviewQueueHandler(AppDbContext db, IScopeContext scope, ISystemSettingReader settings) : IListReviewQueueHandler
{
    /// <summary>Audit actions that mark an application (re)entering the reviewer's active queue -
    /// see ReviewQueueItemDto.EnteredQueueAt's own doc comment for why this isn't CreatedAt.</summary>
    private static readonly string[] ReviewQueueEntryActions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    /// <summary>
    /// Keyed by the same vocabulary the endpoint validates against
    /// (<see cref="ReviewQueueFilterValues.States"/>), and case-SENSITIVE to match it - a map that
    /// accepted "underreview" while the endpoint's allow-list did not would put the two out of step
    /// in the direction that silently widens.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SupplierOnboardingState> StateFilterMap = new Dictionary<string, SupplierOnboardingState>(StringComparer.Ordinal)
    {
        ["Submitted"] = SupplierOnboardingState.Submitted,
        ["UnderReview"] = SupplierOnboardingState.UnderReview,
        ["InfoRequested"] = SupplierOnboardingState.InfoRequested,
        // Decided, and reachable only by asking for them - the default set below is unchanged.
        ["Approved"] = SupplierOnboardingState.Approved,
        ["Rejected"] = SupplierOnboardingState.Rejected,
    };

    public async Task<ListEnvelope<ReviewQueueItemDto>> HandleAsync(string? cursor, int? limit, bool withCount, string? state, string? assignedTo, CancellationToken ct)
    {
        var states = state is not null && StateFilterMap.TryGetValue(state, out var single)
            ? [single]
            : new[] { SupplierOnboardingState.Submitted, SupplierOnboardingState.UnderReview, SupplierOnboardingState.InfoRequested };
        var pageSize = ListEnvelope<ReviewQueueItemDto>.ClampPageSize(limit);

        var query = db.Suppliers.Where(s => states.Contains(s.OnboardingState));

        // FEAT-03.6: "me" resolves against the caller so the frontend never needs to know its own
        // user id; "unassigned" surfaces the pool a reviewer would actually claim from; anything
        // else is treated as a literal reviewer user id (a manager filtering by a specific
        // reviewer).
        if (assignedTo == "me")
        {
            query = query.Where(s => s.AssignedReviewerId == scope.UserId);
        }
        else if (assignedTo == "unassigned")
        {
            query = query.Where(s => s.AssignedReviewerId == null);
        }
        else if (assignedTo is not null && Guid.TryParse(assignedTo, out var reviewerId))
        {
            query = query.Where(s => s.AssignedReviewerId == reviewerId);
        }

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            // Strictly "after" the cursor row in ascending order (oldest submission first, the
            // order a reviewer should work the queue). The Id tie-break is what keeps this safe
            // when two suppliers register in the same tick.
            query = query.Where(s =>
                s.CreatedAt > from.At
                || (s.CreatedAt == from.At && s.Id.CompareTo(from.Id) > 0));
        }

        // limit + 1: the extra row answers HasMore without a COUNT over a queue new applications
        // are inserted into continuously.
        var rows = await query
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.CreatedAt, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, OnboardingState = s.OnboardingState.ToString(), s.AssignedReviewerId })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        // FEAT-03.6: most recent "(re)entered the active queue" audit row per supplier on this
        // page - a second, small query rather than a join on the paged query above, since only
        // the page's own rows (at most pageSize) need it.
        var pageIds = items.Select(r => r.Id).ToList();
        var enteredQueueAtBySupplier = await db.AuditLogs
            .Where(a => a.AggregateType == "Supplier" && pageIds.Contains(a.AggregateId) && ReviewQueueEntryActions.Contains(a.Action))
            .GroupBy(a => a.AggregateId)
            .Select(g => new { SupplierId = g.Key, EnteredAt = g.Max(a => a.OccurredAt) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.EnteredAt, ct);

        var reviewerIds = items.Where(r => r.AssignedReviewerId is not null).Select(r => r.AssignedReviewerId!.Value).Distinct().ToList();
        var reviewerNamesById = await db.Users
            .Where(u => reviewerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        // A-5: the target, computed once per page from the configured working-day SLA.
        var slaWorkingDays = await settings.GetIntAsync(SystemSettings.ReviewSlaWorkingDays, ct);

        var dtos = items
            .Select(r =>
            {
                var enteredAt = enteredQueueAtBySupplier.GetValueOrDefault(r.Id, r.CreatedAt);
                return new ReviewQueueItemDto(
                    r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn, r.OnboardingState,
                    enteredAt,
                    r.AssignedReviewerId,
                    r.AssignedReviewerId is { } rid ? reviewerNamesById.GetValueOrDefault(rid) : null,
                    ReviewSla.TargetFor(enteredAt, slaWorkingDays));
            })
            .ToList();

        return ListEnvelope<ReviewQueueItemDto>.Cursor(
            dtos,
            hasMore,
            hasMore ? new KeysetCursor(items[^1].CreatedAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "createdAt",
            filtersApplied: DescribeFilters(state, assignedTo));
    }
    /// <summary>
    /// The filters actually applied, for the envelope's <c>meta.filtersApplied</c> (§5.2, whose
    /// example renders them as <c>["state=UnderReview,Rejected"]</c>). Null when unfiltered, so a
    /// caller looking at an empty queue can tell "nothing is queued" from "nothing matched".
    /// </summary>
    private static IReadOnlyList<string>? DescribeFilters(string? state, string? assignedTo)
    {
        List<string> applied = [];
        if (state is not null) applied.Add($"state={state}");
        if (assignedTo is not null) applied.Add($"assignedTo={assignedTo}");
        return applied.Count == 0 ? null : applied;
    }
}
