namespace MotsSupplierPortal.Domain.Common;

/// <summary>
/// An aggregate root carrying the <c>xmin</c>-backed row version §8.1's concurrency contract is
/// built on.
///
/// <para>The marker exists so the persistence layer can find the aggregate a request is mutating
/// without every handler having to hand it over. Six roots implement it - Supplier, Rfq, Proposal,
/// Evaluation, EvaluationTemplate, Award - and a mutable root that does NOT is a schema gap rather
/// than an exemption: it cannot be protected from a lost update at all.</para>
/// </summary>
public interface IVersionedAggregate
{
    uint RowVersion { get; }
}
