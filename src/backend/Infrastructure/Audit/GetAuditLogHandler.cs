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
    public async Task<ListEnvelope<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        // Deliberately not available to staff: "own trail" is meaningless without a SupplierId,
        // and returning the global log here would hand every staff caller an unfiltered dump.
        if (scope.SupplierId is null) return ListEnvelope<AuditLogEntryDto>.Empty(ListEnvelope<AuditLogEntryDto>.DefaultPageSize);

        var pageSize = ListEnvelope<AuditLogEntryDto>.ClampPageSize(limit);
        var query = ScopedQuery();

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

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

        return ListEnvelope<AuditLogEntryDto>.Cursor(
            items,
            hasMore,
            hasMore ? new AuditCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "-occurredAt");
    }

    /// <summary>
    /// MSP-75/FR-AUD-004: global search across the whole (row-scoped) log, filterable and
    /// keyset-paged the same way HandleOwnTrailAsync is. The only structural difference is
    /// ApplyFilter being layered on top of ScopedQuery before the cursor predicate - filtering and
    /// paging compose because both are ordinary WHERE clauses over the same IQueryable, applied
    /// before the ORDER BY/Take that Project performs.
    /// </summary>
    public async Task<ListEnvelope<AuditLogEntryDto>> HandleFilteredAsync(
        AuditLogFilter filter, string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        var pageSize = ListEnvelope<AuditLogEntryDto>.ClampPageSize(limit);
        var query = ApplyFilter(ScopedQuery(), filter);

        // §6.1: "totalCount omitted unless ?withCount=true". Counted over the filtered set BEFORE
        // the cursor narrows it - a count of "rows after this cursor" is not a total, and would
        // shrink as the caller pages. A second query, so it is off unless asked for.
        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (AuditCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(a =>
                a.OccurredAt < from.OccurredAt
                || (a.OccurredAt == from.OccurredAt && a.Id.CompareTo(from.Id) < 0));
        }

        var rows = await Project(query).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        return ListEnvelope<AuditLogEntryDto>.Cursor(
            items,
            hasMore,
            hasMore ? new AuditCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "-occurredAt",
            filtersApplied: filter.Describe());
    }

    /// <summary>No Take, no materialization here - EF translates this to a single streamed SELECT
    /// and IAsyncEnumerable hands rows to the caller as Npgsql reads them off the wire, the same
    /// shape MinioFileStorage.OpenReadAsync uses for the same reason (MSP-74).</summary>
    public IAsyncEnumerable<AuditLogEntryDto> StreamForExportAsync(AuditLogFilter filter, CancellationToken ct) =>
        Project(ApplyFilter(ScopedQuery(), filter)).AsAsyncEnumerable();

    /// <summary>
    /// The same rows HandleOwnTrailAsync pages through, unpaged. The scope check is repeated here
    /// rather than delegated: ScopedQuery deliberately returns the WHOLE table for a caller with no
    /// SupplierId, which is right for the staff search and wrong for this route, which is gated on
    /// nothing but being signed in.
    /// </summary>
    public IAsyncEnumerable<AuditLogEntryDto> StreamOwnTrailForExportAsync(CancellationToken ct) =>
        scope.SupplierId is null
            ? AsyncEnumerable.Empty<AuditLogEntryDto>()
            : Project(ScopedQuery()).AsAsyncEnumerable();

    /// <summary>Every predicate here is optional and independently combinable - null on a field
    /// leaves that dimension unfiltered rather than excluding rows. Reuses the three pre-existing
    /// indexes ((AggregateType,AggregateId,OccurredAt), (ActorUserId,OccurredAt), (OccurredAt,Id))
    /// plus the (Action,OccurredAt) index added alongside this handler - Action was the one
    /// dimension none of the pre-existing indexes covered.</summary>
    private static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> query, AuditLogFilter filter)
    {
        if (filter.AggregateType is not null) query = query.Where(a => a.AggregateType == filter.AggregateType);
        if (filter.AggregateId is not null) query = query.Where(a => a.AggregateId == filter.AggregateId);
        if (filter.ActorUserId is not null) query = query.Where(a => a.ActorUserId == filter.ActorUserId);
        if (filter.Action is not null) query = query.Where(a => a.Action == filter.Action);
        // Inclusive both ends - see AuditLogFilter's own doc comment for why.
        if (filter.From is not null) query = query.Where(a => a.OccurredAt >= filter.From);
        if (filter.To is not null) query = query.Where(a => a.OccurredAt <= filter.To);
        return query;
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
