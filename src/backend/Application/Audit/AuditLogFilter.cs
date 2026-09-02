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

    /// <summary>
    /// The filters actually applied, for the envelope's <c>meta.filtersApplied</c>
    /// (API-ARCHITECTURE.md §5.2, whose example renders them as <c>["state=UnderReview,Rejected"]</c>).
    /// Null when nothing was filtered, so <c>meta</c> distinguishes "no filter" from "a filter that
    /// matched nothing" - two states a caller staring at an empty list needs to tell apart.
    ///
    /// <para>Values are the caller's own query values echoed back, never row content, so this cannot
    /// leak audit data into a response the caller could not already see.</para>
    /// </summary>
    public IReadOnlyList<string>? Describe()
    {
        List<string> applied = [];
        if (AggregateType is not null) applied.Add($"aggregateType={AggregateType}");
        if (AggregateId is not null) applied.Add($"aggregateId={AggregateId}");
        if (ActorUserId is not null) applied.Add($"actorUserId={ActorUserId}");
        if (Action is not null) applied.Add($"action={Action}");
        if (From is not null) applied.Add($"from={From:O}");
        if (To is not null) applied.Add($"to={To:O}");
        return applied.Count == 0 ? null : applied;
    }
}
