using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Rfqs;

public abstract record RfqMutationResult
{
    public sealed record Success(RfqDto Rfq) : RfqMutationResult;
    public sealed record NotFoundOrOutOfScope : RfqMutationResult;
    /// <summary>Wraps every Rfq domain-invariant refusal (illegal transition, missing item,
    /// timeline inconsistency, unbound template, etc.) with the exact DomainException message -
    /// same pattern as ProfileMutationResult.InvalidState/EvaluationTemplateMutationResult.InvalidState.</summary>
    public sealed record InvalidState(string Message) : RfqMutationResult;

    /// <summary>
    /// §3: "Illegal transitions return 409 Conflict (type: …/errors/invalid-state-transition)
    /// listing the current state and the allowed next states."
    ///
    /// <para>Distinct from <see cref="InvalidState"/>, which covers every OTHER domain refusal - a
    /// missing item, an unbound template, an inconsistent timeline. Those are 400s about the request;
    /// this one is a 409 about the aggregate's position in its machine, and the difference matters to
    /// a client deciding whether to fix the payload or refetch the resource.</para>
    /// </summary>
    public sealed record IllegalTransition(RfqState CurrentState, string Message) : RfqMutationResult;
    /// <summary>
    /// T-018: this caller may not change this deadline in this direction - an officer shortening, or a
    /// manager extending.
    ///
    /// <para>A 403 rather than a 404. §9.2's hide-existence rule protects against a caller LEARNING
    /// that a row exists; here the caller has already been told, because the row-scope check above
    /// returned the RFQ. Answering 404 at this point would hide the reason for the refusal without
    /// hiding anything else.</para>
    /// </summary>
    public sealed record DeadlineChangeNotPermitted : RfqMutationResult;

    /// <summary>
    /// A-7: the nominated owner or approver cannot hold the role the operation needs them to hold -
    /// they are not a user of this organization, they are deactivated, or they lack the permission.
    ///
    /// <para>A 422 rather than a 400: the request is well-formed and names a real field, and what is
    /// wrong is a fact about the world the client could not have known. Distinct from
    /// <see cref="InvalidState"/> so a screen can say which person was refused rather than repeating
    /// a generic message.</para>
    /// </summary>
    public sealed record IneligibleUser(string Message) : RfqMutationResult;

    public sealed record InvalidCategory : RfqMutationResult;
    public sealed record InvalidUnitOfMeasure : RfqMutationResult;
    public sealed record InvalidEvaluationTemplate(string Message) : RfqMutationResult;
    /// <summary>FEAT-08.1/BRULE-032: the invited (or, on Publish, already-invited) supplier is not
    /// found or not Active.</summary>
    public sealed record SupplierNotActive : RfqMutationResult;
}

/// <summary>Supplier-side self-service result - deliberately does not reuse RfqMutationResult so a
/// supplier-facing 404 can never be confused with the buyer-side NotFoundOrOutOfScope, which is
/// organization-scoped rather than invitation-scoped.</summary>
public abstract record SupplierRfqResult
{
    public sealed record Success(SupplierRfqDto Rfq) : SupplierRfqResult;
    /// <summary>FEAT-08.6/FR-INV-006: the RFQ does not exist, is not yet Published, or this
    /// supplier holds no Invitation to it - all three collapse to the same 404 at the endpoint so a
    /// non-invited supplier cannot distinguish "wrong reference code" from "not invited".</summary>
    public sealed record NotFoundOrNotInvited : SupplierRfqResult;
    public sealed record InvalidState(string Message) : SupplierRfqResult;
}
