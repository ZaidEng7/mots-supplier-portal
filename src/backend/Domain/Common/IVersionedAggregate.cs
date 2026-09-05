namespace MotsSupplierPortal.Domain.Common;

/// <summary>
/// An aggregate root carrying the row version §8.1's concurrency contract is built on.
///
/// <para>The marker exists so the persistence layer can find the aggregate a request is mutating
/// without every handler having to hand it over. <b>Nine</b> roots implement it - Supplier, Rfq,
/// Proposal, Evaluation, EvaluationTemplate, Award, Offering, SupplierFieldConfig and Notification -
/// and a mutable root that does NOT is a schema gap rather than an exemption: it cannot be protected
/// from a lost update at all.</para>
///
/// <para><b>T-030/D-15: this is an APPLICATION-managed counter, not Postgres <c>xmin</c>.</b> It used
/// to be xmin, and that was the defect. xmin advances only when the root ROW is written, and a child
/// insert does not write it - so a correct <c>If-Match</c> on a route that only touches children was
/// silently skipped, and two callers editing different children of one aggregate both won. xmin
/// cannot be assigned either, so there was no way to force it forward from the application. A
/// counter the application owns can be bumped deliberately, which is the whole point.</para>
///
/// <para>The wire contract is unchanged: still a <c>uint</c>, still base64url in a strong ETag, still
/// compared byte-for-byte against <c>If-Match</c>. Only where the number comes from has moved. The
/// setter stays private on every root - the persistence layer writes it through EF's property API in
/// <c>AppDbContext.SaveChangesAsync</c>, so no domain method exists that a handler could call to fake
/// a version.</para>
/// </summary>
public interface IVersionedAggregate
{
    uint RowVersion { get; }
}
