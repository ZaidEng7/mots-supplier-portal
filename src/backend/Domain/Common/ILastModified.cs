namespace MotsSupplierPortal.Domain.Common;

/// <summary>
/// An aggregate root that records when it last changed.
///
/// <para><b>T-003.</b> §12.2 documents <c>updatedAt</c> on the supplier profile and nothing computed
/// it, because nothing stored it: the aggregate had <c>CreatedAt</c> and a row version, and a version
/// answers "has this changed since I read it" without answering "when". A client deciding whether to
/// re-fetch, a reviewer asking how stale a profile is, and any future conditional GET all need the
/// instant rather than the counter.</para>
///
/// <para><b>Stamped where the version is bumped, not by each handler.</b> <c>AppDbContext</c> already
/// identifies every versioned root a unit of work touches - directly or through a child - and
/// advances its version there. The moment a root's version advances is exactly the moment it was last
/// modified, so the two facts are written by the same code and cannot disagree. Thirty-two call sites
/// setting a timestamp by hand is how they would.</para>
///
/// <para><b>Only Supplier implements it today</b>, because only §12.2 asks for it. Any root that adds
/// the interface is stamped with no further change - which is the point of putting it here rather
/// than special-casing one type in the persistence layer.</para>
/// </summary>
public interface ILastModified
{
    DateTimeOffset UpdatedAt { get; }
}
