// When an application last entered the review queue, which is what its review target is measured from.
//
// Not the supplier's creation date. An application can enter the active queue more than once: submitted,
// resubmitted after an information request, resumed, or pushed back in because a compliance field changed. The
// most recent of those, read from the audit trail, is the moment it entered, and a supplier with none of them on
// the trail falls back to its creation date.
//
// The reviewer's queue (ListReviewQueueHandler) and the administrator's dashboard (NeedsAttentionSectionHandler)
// both read it from here, so the target a reviewer sees on a row and the overdue count an administrator sees are
// measured from the same moment.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class ReviewQueueEntry
{
    public static readonly string[] Actions =
    [
        "application_submitted", "application_resubmitted", "application_review_resumed",
        "compliance_field_changed_review_retriggered",
    ];

    public static Task<Dictionary<Guid, DateTimeOffset>> LatestBySupplierAsync(
        AppDbContext db, IReadOnlyCollection<Guid> supplierIds, CancellationToken ct) =>
        LatestAsync(db.AuditLogs.Where(a => supplierIds.Contains(a.AggregateId)), ct);

    public static Task<Dictionary<Guid, DateTimeOffset>> LatestBySupplierAsync(
        AppDbContext db, IQueryable<Guid> supplierIds, CancellationToken ct) =>
        LatestAsync(db.AuditLogs.Where(a => supplierIds.Contains(a.AggregateId)), ct);

    public static DateTimeOffset EnteredAt(
        IReadOnlyDictionary<Guid, DateTimeOffset> latest, Guid supplierId, DateTimeOffset createdAt) =>
        latest.GetValueOrDefault(supplierId, createdAt);

    private static Task<Dictionary<Guid, DateTimeOffset>> LatestAsync(IQueryable<AuditLog> rows, CancellationToken ct) =>
        rows
            .Where(a => a.AggregateType == "Supplier" && Actions.Contains(a.Action))
            .GroupBy(a => a.AggregateId)
            .Select(g => new { SupplierId = g.Key, EnteredAt = g.Max(a => a.OccurredAt) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.EnteredAt, ct);
}
