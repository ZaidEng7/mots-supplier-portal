// The onboarding reviewer's dashboard: the aggregates over the queue that already exists.
//
// Presentation rather than a second query path. The list itself is still served by the queue handler, and this
// adds only the figures the screen needs.
//
//
// ONBOARDING REVIEW IS NOT ORGANIZATION-SCOPED, AND THAT IS NOT AN OVERSIGHT
//
// A supplier onboards onto the platform rather than into one buying body, and a supplier carries no organization at
// all, so the reviewer's queue has never had that dimension and neither does this.
//
// The scope that matters here is the permission, and the negative tests assert that rather than inventing a tenant
// boundary the domain does not have.
//
//
// THE AGE IS MEASURED THE SAME WAY THE QUEUE MEASURES IT
//
// From the most recent audit row marking the application entering the active queue.
//
// A supplier has no submission timestamp, and its creation date is the registration date, which would make a
// long-registered supplier who resubmitted yesterday read as months old.
//
// Computing the ageing a second way would put this dashboard and the queue's own age column out of step, which is
// worse than either number alone. The list of actions that count as entering the queue is duplicated from the queue
// handler deliberately rather than shared, because they are private there and the two screens agreeing is asserted
// by a test rather than by a reference. If they drift, that test is what says so.
//
// The watchlist is bounded, like every other list in this codebase. One that grew with the tenant would turn one
// slow dashboard into a slower one; the full picture lives on the documents screen.

namespace MotsSupplierPortal.Infrastructure.Dashboards;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ReviewDashboardHandler(AppDbContext db, IScopeContext scope) : IReviewDashboardHandler
{
    private static readonly string[] ReviewQueueEntryActions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    private static readonly SupplierOnboardingState[] OpenStates =
    [
        SupplierOnboardingState.Submitted,
        SupplierOnboardingState.UnderReview,
        SupplierOnboardingState.InfoRequested,
    ];

    public async Task<ReviewDashboardDto> HandleAsync(CancellationToken ct)
    {
        var open = db.Suppliers.AsNoTracking().Where(s => OpenStates.Contains(s.OnboardingState));

        var byState = await open
            .GroupBy(s => s.OnboardingState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.State, g => g.Count, ct);

        var openSupplierIds = await open.Select(s => s.Id).ToListAsync(ct);

        var enteredQueueAt = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateType == "Supplier"
                        && openSupplierIds.Contains(a.AggregateId)
                        && ReviewQueueEntryActions.Contains(a.Action))
            .GroupBy(a => a.AggregateId)
            .Select(g => g.Max(a => a.OccurredAt))
            .ToListAsync(ct);

        DateTimeOffset? oldest = enteredQueueAt.Count == 0 ? null : enteredQueueAt.Min();

        var watchlist = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.IsLatestVersion
                        && (d.State == DocumentState.ExpiringSoon || d.State == DocumentState.Expired))
            .OrderBy(d => d.ExpiryDate)
            .Take(WatchlistSize)
            .Select(d => new ExpiringDocumentDto(
                db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.ReferenceCode).First(),
                db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.DisplayNameAr).First(),
                db.Suppliers.Where(s => s.Id == d.SupplierId).Select(s => s.DisplayNameEn).First(),
                db.DocumentTypes.Where(t => t.Id == d.DocumentTypeId).Select(t => t.Code).First(),
                d.State.ToString(),
                d.ExpiryDate))
            .ToListAsync(ct);

        return new ReviewDashboardDto(
            Pending: byState.GetValueOrDefault(SupplierOnboardingState.Submitted),
            UnderReview: byState.GetValueOrDefault(SupplierOnboardingState.UnderReview),
            InfoRequested: byState.GetValueOrDefault(SupplierOnboardingState.InfoRequested),
            Unassigned: await open.CountAsync(s => s.AssignedReviewerId == null, ct),
            AssignedToMe: scope.UserId is { } userId
                ? await open.CountAsync(s => s.AssignedReviewerId == userId, ct)
                : 0,
            OldestOpenCaseAgeDays: oldest is { } enteredAt
                ? (int)(DateTimeOffset.UtcNow - enteredAt).TotalDays
                : null,
            ExpiryWatchlist: watchlist);
    }

    private const int WatchlistSize = 25;
}
