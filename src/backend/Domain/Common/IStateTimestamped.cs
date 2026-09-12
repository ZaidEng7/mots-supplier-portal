namespace MotsSupplierPortal.Domain.Common;

/// <summary>
/// An aggregate that records when it entered its current state.
///
/// <para><b>T-031.</b> Nothing stored this, so "how long has this been sitting here" was not a
/// question the database could answer. The approval queue shows it: its RFQ rows returned a null
/// waiting-since with a comment explaining that the only available dates were wrong -
/// <c>CreatedAt</c> would read as "waiting three weeks" for a tender drafted three weeks ago and
/// submitted yesterday, and <c>RfqApproval.DecidedAt</c> records the end of the wait rather than its
/// start.</para>
///
/// <para><b>Why not the audit log.</b> The supplier review queue derives its own ageing from audit
/// rows, and that works - at the cost of a query per row, and of a queue that silently reads
/// "just entered" when an expected audit row is absent. A column on the aggregate is one read, cannot
/// be missing, and is the thing the backlog row asked for.</para>
///
/// <para><b>Stamped by the persistence layer, not by each transition.</b> <c>AppDbContext</c> can see
/// which properties a unit of work modified, so it stamps this whenever the named state property
/// actually changes. The alternative is a line in every transition method - <c>Rfq</c> alone has
/// seventeen - and the one that gets forgotten is invisible until somebody reads a queue and believes
/// a wrong number.</para>
///
/// <para><b>Nullable, and null means "before this column existed".</b> Rows that predate the migration
/// have no honest value: the instant they entered their current state was not recorded anywhere, and
/// backfilling from a creation date would manufacture exactly the wrong answer the queue's own comment
/// warned about. A consumer shows an age when there is one and says nothing when there is not.</para>
/// </summary>
public interface IStateTimestamped
{
    DateTimeOffset? StateChangedAt { get; }

    /// <summary>
    /// The name of the property whose change is a state change.
    ///
    /// <para>Static and abstract so it is declared once by the type itself rather than passed around
    /// as a string: an aggregate whose state lives in <c>OnboardingState</c> and one whose state lives
    /// in <c>State</c> both satisfy this without the persistence layer knowing either name.</para>
    /// </summary>
    static abstract string StatePropertyName { get; }
}
