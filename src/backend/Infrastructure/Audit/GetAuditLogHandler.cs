using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Audit;

/// <summary>
/// MSP-62: row-scoped audit read. Before this, the handler filtered on AggregateId alone, so any
/// holder of audit.read could pull ANY aggregate's trail by GUID - including named actors
/// (ActorLabel) and reviewer free text (Reason). Permission answered "may you read audit", never
/// "whose audit", which is the BRULE-084/094 distinction.
///
/// Scope model (canonical §6):
/// - Supplier-scoped caller  -> only aggregates owned by their SupplierId.
/// - Staff / global caller   -> unrestricted, which is BRULE-092's deliberate system_admin grant.
///
/// Scoping is applied IN the query (subqueries, not post-filtering) per STORY-01.8.1 AC4.
/// </summary>
public sealed class GetAuditLogHandler(AppDbContext db, IScopeContext scope) : IGetAuditLogHandler
{
    public async Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct) =>
        await Project(ScopedQuery().Where(a => a.AggregateId == aggregateId)).ToListAsync(ct);

    /// <summary>
    /// Keyset-paged (MSP-66). See AuditCursor for why keyset rather than offset on this table
    /// specifically, and why the cursor carries Id as well as OccurredAt.
    /// </summary>
    public async Task<Page<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, CancellationToken ct)
    {
        // Deliberately not available to staff: "own trail" is meaningless without a SupplierId,
        // and returning the global log here would hand every staff caller an unfiltered dump.
        if (scope.SupplierId is null) return new Page<AuditLogEntryDto>([], false);

        var pageSize = Page<AuditLogEntryDto>.ClampLimit(limit);
        var query = ScopedQuery();

        if (AuditCursor.TryDecode(cursor, out var from))
        {
            // Strictly "after" the cursor row in the sort order. The Id comparison is what makes a
            // shared timestamp safe: without it, rows tied on OccurredAt are dropped or repeated at
            // the page boundary, and one request routinely writes several rows at the same instant.
            query = query.Where(a =>
                a.OccurredAt < from.OccurredAt
                || (a.OccurredAt == from.OccurredAt && a.Id.CompareTo(from.Id) < 0));
        }

        // limit + 1: the extra row is how HasMore is answered without a COUNT over a table that
        // grows forever.
        var rows = await Project(query).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        return new Page<AuditLogEntryDto>(
            items,
            hasMore,
            hasMore ? new AuditCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null);
    }

    private IQueryable<AuditLog> ScopedQuery()
    {
        if (scope.SupplierId is not { } supplierId)
        {
            return db.AuditLogs;
        }

        // The three aggregate types a supplier can own. Anything else (or a type added later
        // without being listed here) falls outside the predicate and is therefore invisible to
        // supplier callers - failing closed rather than open, which is the right default for a
        // list that will grow.
        var ownedDocumentIds = db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId)
            .Select(d => d.Id);

        var ownedUserIds = db.Users
            .Where(u => u.SupplierId == supplierId)
            .Select(u => u.Id);

        return db.AuditLogs.Where(a =>
            (a.AggregateType == "Supplier" && a.AggregateId == supplierId) ||
            (a.AggregateType == "SupplierDocument" && ownedDocumentIds.Contains(a.AggregateId)) ||
            (a.AggregateType == "User" && ownedUserIds.Contains(a.AggregateId)));
    }

    private static IQueryable<AuditLogEntryDto> Project(IQueryable<AuditLog> query) =>
        query
            // Id is part of the ordering, not decoration: it is the keyset tie-break, and the sort
            // must match the cursor predicate exactly or paging skips rows.
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogEntryDto(
                a.Id,
                a.OccurredAt, a.AggregateType, a.AggregateId, a.Action, a.FromState, a.ToState, a.ActorLabel));
}
