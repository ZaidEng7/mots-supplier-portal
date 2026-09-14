// The vocabulary for reading the audit trail, in five shapes for five different questions.
//
// The row's own identifier is on the wire because it is half the paging cursor. Without it a caller
// cannot ask for the next page.
//
//
// EVERY READ IS BOTH GATED AND SCOPED
//
// The permission answers whether a caller may read the audit trail at all. It does not answer whose.
// A supplier-scoped caller asking about a record outside their own company gets an empty result and
// never another supplier's trail.
//
//
// THE FIVE READS
//
// HandleAsync is one record's trail, for a caller who already knows which record.
//
// HandleOwnTrailAsync is a supplier's whole trail across everything it owns, with no record named. That
// is the half of the requirement that says suppliers see their own activity.
//
// HandleFilteredAsync is the staff-facing search across records, behind the same permission as the
// single-record read, because it is that same authority applied broadly rather than to one row. It is
// paged by cursor for the same reason the supplier's trail is: this table is kept indefinitely and grows
// without bound.
//
// StreamForExportAsync is the export of that search. Same filter, same scoping, and no page limit,
// because an export is everything the filter matches rather than the current page. It is streamed rather
// than assembled, so an export bounded only by its filter does not have to fit in memory.
//
// StreamOwnTrailForExportAsync is the supplier's own export, scoped exactly as their list is.
//
//
// WHY THE TWO EXPORTS ARE SEPARATE
//
// This is the important one. The staff export's scoping falls open for a caller with no company, because
// for a staff caller unrestricted is the correct answer.
//
// Reached through the supplier's own route it would be the wrong answer: an export handing a staff
// caller the entire audit table from a route gated only on being signed in. So the supplier's export is
// its own method, yielding nothing when there is no company scope, which matches its own list rather
// than inventing a second behaviour.

namespace MotsSupplierPortal.Application.Audit;

using MotsSupplierPortal.Application.Common;

public sealed record AuditLogEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string AggregateType,
    Guid AggregateId,
    string Action,
    string? FromState,
    string? ToState,
    string? ActorLabel);

public interface IGetAuditLogHandler
{
    Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct);

    Task<ListEnvelope<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, bool withCount, CancellationToken ct);

    Task<ListEnvelope<AuditLogEntryDto>> HandleFilteredAsync(AuditLogFilter filter, string? cursor, int? limit, bool withCount, CancellationToken ct);

    IAsyncEnumerable<AuditLogEntryDto> StreamForExportAsync(AuditLogFilter filter, CancellationToken ct);

    IAsyncEnumerable<AuditLogEntryDto> StreamOwnTrailForExportAsync(CancellationToken ct);
}
