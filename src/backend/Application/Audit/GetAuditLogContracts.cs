using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Audit;

public sealed record AuditLogEntryDto(
    // Id is exposed because it is half the keyset cursor; without it a caller cannot page.
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
    /// <summary>Gated by audit.read (STORY-01.7.1) AND row-scoped (BRULE-084/094, FR-AUD-003):
    /// a supplier-scoped caller reading an aggregate outside their own SupplierId gets an empty
    /// result, never another supplier's trail. Permission alone is not sufficient - it answers
    /// "may you read audit at all", not "whose".</summary>
    Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct);

    /// <summary>FR-AUD-003 second half: "suppliers see their own activity trail". The caller's own
    /// supplier's full trail across every aggregate it owns, with no aggregate id needed.</summary>
    Task<ListEnvelope<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, bool withCount, CancellationToken ct);

    /// <summary>FR-AUD-004/MSP-75: staff-facing global search, gated by audit.read (same permission
    /// as <see cref="HandleAsync"/> - this is that same authority applied across aggregates instead
    /// of to one). Filterable by entity, actor, action, and date range (<see cref="AuditLogFilter"/>),
    /// combinable, and keyset-paged for the same reason HandleOwnTrailAsync is (ASM-085: this table
    /// is retained indefinitely and grows without bound).</summary>
    Task<ListEnvelope<AuditLogEntryDto>> HandleFilteredAsync(AuditLogFilter filter, string? cursor, int? limit, bool withCount, CancellationToken ct);

    /// <summary>The export path for the same search: same filter, same row-scoping, no page limit -
    /// an export is defined as "everything the filter matches", not "the current page". Streamed
    /// rather than materialized, matching the streaming discipline from MSP-74/NFR-PERF-008: an
    /// export bounded only by its filter must not buffer the whole result set in memory to produce
    /// it.</summary>
    IAsyncEnumerable<AuditLogEntryDto> StreamForExportAsync(AuditLogFilter filter, CancellationToken ct);
}
