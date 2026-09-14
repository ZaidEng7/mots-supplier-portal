// Reads the audit trail, scoped to what the caller may see.
//
// Before this, it filtered on the record's identifier alone, so anybody holding the audit permission could
// pull any record's trail by identifier, including named actors and reviewers' free text.
//
// The permission answered whether you may read audit at all. It never answered whose, and those are two
// different questions.
//
// A supplier-scoped caller sees only records owned by their own company. A staff caller is unrestricted,
// which is the deliberate grant for that role.
//
// The scoping is applied inside the query rather than by filtering results afterwards, so a row the caller
// may not see is never fetched in the first place.

namespace MotsSupplierPortal.Infrastructure.Audit;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetAuditLogHandler(AppDbContext db, IScopeContext scope) : IGetAuditLogHandler
{
    public async Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct) =>
        await Project(ScopedQuery().Where(a => a.AggregateId == aggregateId)).ToListAsync(ct);

    public async Task<ListEnvelope<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        if (scope.SupplierId is null) return ListEnvelope<AuditLogEntryDto>.Empty(ListEnvelope<AuditLogEntryDto>.DefaultPageSize);

        var pageSize = ListEnvelope<AuditLogEntryDto>.ClampPageSize(limit);
        var query = ScopedQuery();

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(a =>
                a.OccurredAt < from.At
                || (a.OccurredAt == from.At && a.Id.CompareTo(from.Id) < 0));
        }

        var rows = await Project(query).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        return ListEnvelope<AuditLogEntryDto>.Cursor(
            items,
            hasMore,
            hasMore ? new KeysetCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "-occurredAt");
    }

    public async Task<ListEnvelope<AuditLogEntryDto>> HandleFilteredAsync(
        AuditLogFilter filter, string? cursor, int? limit, bool withCount, CancellationToken ct)
    {
        var pageSize = ListEnvelope<AuditLogEntryDto>.ClampPageSize(limit);
        var query = ApplyFilter(ScopedQuery(), filter);

        int? totalCount = withCount ? await query.CountAsync(ct) : null;

        if (KeysetCursor.TryDecode(cursor, out var from))
        {
            query = query.Where(a =>
                a.OccurredAt < from.At
                || (a.OccurredAt == from.At && a.Id.CompareTo(from.Id) < 0));
        }

        var rows = await Project(query).Take(pageSize + 1).ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;

        return ListEnvelope<AuditLogEntryDto>.Cursor(
            items,
            hasMore,
            hasMore ? new KeysetCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null,
            pageSize,
            totalCount,
            sort: "-occurredAt",
            filtersApplied: filter.Describe());
    }

    public IAsyncEnumerable<AuditLogEntryDto> StreamForExportAsync(AuditLogFilter filter, CancellationToken ct) =>
        Project(ApplyFilter(ScopedQuery(), filter)).AsAsyncEnumerable();

    public IAsyncEnumerable<AuditLogEntryDto> StreamOwnTrailForExportAsync(CancellationToken ct) =>
        scope.SupplierId is null
            ? AsyncEnumerable.Empty<AuditLogEntryDto>()
            : Project(ScopedQuery()).AsAsyncEnumerable();

    private static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> query, AuditLogFilter filter)
    {
        if (filter.AggregateType is not null) query = query.Where(a => a.AggregateType == filter.AggregateType);
        if (filter.AggregateId is not null) query = query.Where(a => a.AggregateId == filter.AggregateId);
        if (filter.ActorUserId is not null) query = query.Where(a => a.ActorUserId == filter.ActorUserId);
        if (filter.Action is not null) query = query.Where(a => a.Action == filter.Action);
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
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogEntryDto(
                a.Id,
                a.OccurredAt, a.AggregateType, a.AggregateId, a.Action, a.FromState, a.ToState, a.ActorLabel));
}
