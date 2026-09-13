// A record that remembers when it entered its current state, so a queue can say how long
// something has been waiting.
//
// StateChangedAt is null when the row is older than this feature. That instant was never
// recorded for those rows, so a reader shows an age when there is one and says nothing
// when there is not. Backfilling from a creation date would manufacture the wrong answer:
// a tender drafted three weeks ago and submitted yesterday would read as "waiting three
// weeks".
//
// The stamp is written by the persistence layer, not by each transition method.
// AppDbContext can see which properties a save actually changed, so it stamps this
// whenever the named state property moves. The alternative is a line in every transition -
// Rfq alone has seventeen - and the one that gets forgotten is invisible until somebody
// reads a queue and believes a wrong number.
//
// StatePropertyName is static and abstract so each record declares its own state property
// by name. A record whose state lives in OnboardingState and one whose state lives in
// State both satisfy this, and the persistence layer needs to know neither name.
//
// The audit log could answer the same question, and the supplier review queue does derive
// its ageing that way. That costs a query per row, and reads "just arrived" whenever an
// expected audit row is missing. A column on the record is one read and cannot go absent.

namespace MotsSupplierPortal.Domain.Common;

public interface IStateTimestamped
{
    DateTimeOffset? StateChangedAt { get; }

    static abstract string StatePropertyName { get; }
}
