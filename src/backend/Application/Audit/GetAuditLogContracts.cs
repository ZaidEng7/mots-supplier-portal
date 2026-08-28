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
    Task<Page<AuditLogEntryDto>> HandleOwnTrailAsync(string? cursor, int? limit, CancellationToken ct);
}
