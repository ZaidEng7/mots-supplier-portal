// The reviewer's queue: applications waiting for somebody to decide them.
//
//
// WHAT COUNTS AS ENTERING THE QUEUE
//
// Not the supplier's creation date. An application can enter the active queue more than once: submitted,
// resubmitted after an information request, resumed, or pushed back in because a compliance field changed.
//
// The queue's age and its service target are measured from the most recent of those, read from the audit
// trail. Measuring from creation would show a resubmitted application as weeks old on the day it arrived.
//
// That is a second small query over the page's own rows rather than a join on the paged query, because at
// most one page's worth of suppliers need it.
//
//
// THE DEFAULT SET, AND THE DECIDED ONES
//
// By default the queue holds the three undecided states. Approved and rejected are reachable only by asking
// for them by name, which is what lets a reviewer look again at a decision they already made without the
// queue filling up with finished work.
//
// The state names are matched case-sensitively, deliberately, because the endpoint's own list of accepted
// values is. A map here that accepted a differently-cased name while the endpoint's list did not would put
// the two out of step in the direction that silently widens.
//
//
// THE ASSIGNMENT FILTER
//
// "Me" resolves against the caller, so the interface never needs to know its own user identifier.
// "Unassigned" surfaces the pool a reviewer would actually claim from. Anything else is treated as a
// literal reviewer identifier, which is a manager filtering by one person.
//
//
// PAGING
//
// Oldest submission first, which is the order a reviewer should work a queue, with the identifier as
// tiebreak so two registrations in the same tick cannot repeat or vanish across a page boundary.
//
// One row beyond the page is fetched to answer "is there more" without counting a queue that new
// applications are inserted into continuously. The total is a second query and is off unless asked for,
// and it is counted before the cursor narrows anything: a count of rows after the cursor is not a total,
// and would shrink as the caller pages.
//
// The applied filters are echoed on the envelope, and are null when nothing was filtered, so a caller
// looking at an empty queue can tell "nothing is queued" from "nothing matched".

namespace MotsSupplierPortal.Infrastructure.Suppliers;

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

public sealed class ListReviewQueueHandler(AppDbContext db, IScopeContext scope, ISystemSettingReader settings) : IListReviewQueueHandler
{
    private static readonly string[] ReviewQueueEntryActions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    private static readonly IReadOnlyDictionary<string, SupplierOnboardingState> StateFilterMap = new Dictionary<string, SupplierOnboardingState>(StringComparer.Ordinal)
    {
        ["Submitted"] = SupplierOnboardingState.Submitted,
        ["UnderReview"] = SupplierOnboardingState.UnderReview,
        ["InfoRequested"] = SupplierOnboardingState.InfoRequested,
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

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(s =>
                s.CreatedAt > from.At
                || (s.CreatedAt == from.At && s.Id.CompareTo(from.Id) > 0));
        }

        var rows = await query
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.CreatedAt, s.ReferenceCode, s.DisplayNameAr, s.DisplayNameEn, OnboardingState = s.OnboardingState.ToString(), s.AssignedReviewerId })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

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
    private static IReadOnlyList<string>? DescribeFilters(string? state, string? assignedTo)
    {
        List<string> applied = [];
        if (state is not null) applied.Add($"state={state}");
        if (assignedTo is not null) applied.Add($"assignedTo={assignedTo}");
        return applied.Count == 0 ? null : applied;
    }
}
