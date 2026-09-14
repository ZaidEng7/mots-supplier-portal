// The dimensions a caller may narrow an audit search by: the kind of record, one record, one actor, one
// action, and a date range.
//
// Every field is optional and they combine. An absent value means unfiltered on that dimension rather
// than matching an absent value.
//
// The date range includes both ends. These are exact instants rather than calendar dates, so there is no
// whole-day boundary to resolve: a row landing exactly on either bound is included. A caller wanting an
// exclusive bound moves it by one tick, and the API does not need to invent that for them.
//
// Describe reports which filters were actually applied, for the response envelope. It is absent when
// nothing was filtered, so a caller staring at an empty list can tell no filter from a filter that
// matched nothing, which are two different situations.
//
// The values echoed there are the caller's own query values, never row content, so this cannot leak
// audit data into a response the caller could not already see.

namespace MotsSupplierPortal.Application.Audit;

public sealed record AuditLogFilter(
    string? AggregateType,
    Guid? AggregateId,
    Guid? ActorUserId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To)
{
    public static readonly AuditLogFilter None = new(null, null, null, null, null, null);

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
