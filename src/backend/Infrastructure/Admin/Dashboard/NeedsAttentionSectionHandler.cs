// The needs attention section of the administrator's dashboard, computed from the other five sections' results.
//
// It runs after the other five have finished and is handed all of their results, hidden and failed included, as
// GetAdminDashboardHandler describes. Which links a viewer may follow comes from the viewer's permissions in the
// request, which were read once before any section started. The rules that turn those results into items live in
// DashboardNeedsAttention, as a pure function, and are described there.
//
//
// THE ONE FIGURE THIS SECTION COUNTS ITSELF: THE REVIEW DEADLINE
//
// No other section counts the onboarding applications that are past their review target, so this one does, in the
// database context of the scope it was resolved from (Q2, which amends A-5).
//
// It counts suppliers in Submitted or UnderReview whose target, ReviewSla.TargetFor(entered the queue,
// review.slaWorkingDays), is before the moment the dashboard was asked for. The target is in working days, the
// same target the reviewer's queue shows on each row, and the moment the application entered the queue is read
// by ReviewQueueEntry, the same helper that queue uses. InfoRequested is left out, because the timer pauses while
// the supplier is asked for more. The queue's own 48-hour and 120-hour calendar tones are not used: they are a
// reviewer's colour for age, not the target.
//
// Every queued supplier is read, as an id and a creation date, because the target is walked one working day at a
// time and is not something the database can compare. The queue is the size of the applications waiting for a
// decision, which is small.
//
// If that count throws, the exception is logged and the check is named in ChecksNotRun. Every other check still
// answers, since they are computed from results already in hand and cannot be failed by a query of this one's.
// Cancellation propagates, as everywhere on the dashboard.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class NeedsAttentionSectionHandler(
    AppDbContext db,
    ISystemSettingReader settings,
    ILogger<NeedsAttentionSectionHandler> logger)
    : INeedsAttentionSectionHandler
{
    public async Task<DashboardNeedsAttentionDto> RunAsync(
        DashboardRequest request, DashboardSectionResults others, CancellationToken ct)
    {
        int? overdueReviews;

        try
        {
            overdueReviews = await CountOverdueReviewsAsync(request.AsOf, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(exception, "The admin dashboard's review deadline check failed.");
            overdueReviews = null;
        }

        return DashboardNeedsAttention.Compute(request.Viewer, others, overdueReviews);
    }

    private async Task<int> CountOverdueReviewsAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var queued = db.Suppliers.Where(s =>
            s.OnboardingState == SupplierOnboardingState.Submitted
            || s.OnboardingState == SupplierOnboardingState.UnderReview);

        var suppliers = await queued
            .AsNoTracking()
            .Select(s => new { s.Id, s.CreatedAt })
            .ToListAsync(ct);

        var entered = await ReviewQueueEntry.LatestBySupplierAsync(db, queued.Select(s => s.Id), ct);
        var slaWorkingDays = await settings.GetIntAsync(SystemSettings.ReviewSlaWorkingDays, ct);

        return suppliers.Count(s =>
            ReviewSla.TargetFor(ReviewQueueEntry.EnteredAt(entered, s.Id, s.CreatedAt), slaWorkingDays) < asOf);
    }
}
