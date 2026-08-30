namespace MotsSupplierPortal.Application.Audit;

/// <summary>
/// MSP-75/FR-AUD-004: the four dimensions a caller may narrow the global audit search by. Every
/// field is optional and combinable - null means "unfiltered on this dimension", not "match null".
///
/// <para><b>Date range is inclusive on both ends.</b> <c>From</c> and <c>To</c> are exact instants
/// (<see cref="DateTimeOffset"/>, not calendar dates), so there is no ambiguity to resolve the way
/// there would be for a whole-day boundary: a row with <c>OccurredAt == From</c> or
/// <c>OccurredAt == To</c> is included in both cases. A caller wanting an exclusive bound passes a
/// tick later/earlier - the API does not need to invent that for them.</para>
/// </summary>
public sealed record AuditLogFilter(
    string? AggregateType,
    Guid? AggregateId,
    Guid? ActorUserId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To)
{
    public static readonly AuditLogFilter None = new(null, null, null, null, null, null);
}
