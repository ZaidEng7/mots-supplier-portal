using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Dashboards;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Dashboards;

/// <summary>
/// SCR-300 / FR-DSH-002. Presentation over the queue PR #80 already built, not a second query path:
/// the list itself is still served by <c>ListReviewQueueHandler</c>, and this adds only the
/// aggregates that screen needs.
///
/// <para><b>Onboarding review is not organization-scoped, and that is not an oversight.</b> A
/// supplier is onboarding onto the platform rather than into one buying entity - <c>Supplier</c> has
/// no <c>OrganizationId</c> - so the reviewer queue has never had an org dimension and neither does
/// this. The scope that matters here is the permission, and the negative tests assert that instead
/// of inventing a tenant boundary the domain does not have.</para>
/// </summary>
public sealed class ReviewDashboardHandler(AppDbContext db, IScopeContext scope) : IReviewDashboardHandler
{
    /// <summary>
    /// The audit actions PR #80's queue treats as "(re)entered the active queue". Duplicated from
    /// ListReviewQueueHandler deliberately rather than shared: they are private there, and the two
    /// screens agreeing is asserted by a test rather than by a reference. If they drift, that test
    /// is what says so.
    /// </summary>
    private static readonly string[] ReviewQueueEntryActions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    /// <summary>The three states PR #80's queue already treats as "open" - kept identical on purpose.</summary>
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

        // The SAME clock PR #80's queue uses: the most recent audit row marking this application
        // (re)entering the active queue. Supplier has no SubmittedAt, and CreatedAt is registration
        // date - which would make a long-registered supplier who resubmitted yesterday read as
        // months old. Computing aging a second way would put the dashboard and the queue's own age
        // column out of step, which is worse than either number alone.
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

    /// <summary>
    /// Bounded, like every other list in this codebase. A watchlist that grows with the tenant turns
    /// one slow dashboard into a slower one; the full picture lives on the documents screen.
    /// </summary>
    private const int WatchlistSize = 25;
}
