// A record that remembers when it last changed, as a date rather than as a counter.
//
// IVersionedAggregate answers "has this changed since I read it". This answers "when",
// which is what a client deciding whether to re-fetch, a reviewer asking how stale a
// profile is, and any future conditional read all need.
//
// The stamp is written where the version is bumped, in AppDbContext, not by each handler.
// The moment a record's version advances is exactly the moment it was last modified, so
// the two facts are written by the same code and cannot disagree. Thirty-two handlers
// setting a timestamp by hand is how they would.
//
// Only Supplier implements it today, because only the supplier profile is documented as
// carrying updatedAt. Any record that adds the interface is stamped with no further
// change, which is the reason this lives here instead of being special-cased in the
// persistence layer.

namespace MotsSupplierPortal.Domain.Common;

public interface ILastModified
{
    DateTimeOffset UpdatedAt { get; }
}
