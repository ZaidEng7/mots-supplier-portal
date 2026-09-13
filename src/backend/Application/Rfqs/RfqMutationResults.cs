// What a tender write can answer.
//
//
// WHY THERE ARE TWO KINDS OF REFUSAL
//
// An illegal state change is its own outcome, carrying the current state, so the API can name what may
// legally follow.
//
// Every other refusal the domain makes, a missing item, an unbound template, an inconsistent timeline,
// arrives as one outcome carrying the domain's own message.
//
// The difference matters to a client deciding whether to fix the payload or fetch the resource again. The
// first is about the request; the second is about the tender's position in its lifecycle.
//
//
// TWO REFUSALS THAT ARE NEITHER
//
// A deadline move in a direction this caller may not make is its own outcome, because the caller can
// demonstrably see the tender and hiding the reason would leave an officer unable to tell a wrong direction
// from something broken.
//
// An ineligible new owner is its own outcome too: the payload is well-formed and names a real person, and
// what is wrong is a fact about that person the client could not have known.
//
//
// THE SUPPLIER'S OUTCOMES ARE A SEPARATE TYPE
//
// A supplier who was not invited must get the same answer as one asking about a tender that does not exist. A
// shared type would eventually grow an outcome that distinguishes them.

namespace MotsSupplierPortal.Application.Rfqs;

using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Application.Common;

public abstract record RfqMutationResult
{
    public sealed record Success(RfqDto Rfq) : RfqMutationResult;
    public sealed record NotFoundOrOutOfScope : RfqMutationResult;
    public sealed record InvalidState(string Message) : RfqMutationResult;

    public sealed record IllegalTransition(RfqState CurrentState, string Message) : RfqMutationResult;
    public sealed record DeadlineChangeNotPermitted : RfqMutationResult;

    public sealed record IneligibleUser(string Message) : RfqMutationResult;

    public sealed record InvalidCategory : RfqMutationResult;
    public sealed record InvalidUnitOfMeasure : RfqMutationResult;
    public sealed record InvalidEvaluationTemplate(string Message) : RfqMutationResult;
    public sealed record SupplierNotActive : RfqMutationResult;
}

public abstract record SupplierRfqResult
{
    public sealed record Success(SupplierRfqDto Rfq) : SupplierRfqResult;
    public sealed record NotFoundOrNotInvited : SupplierRfqResult;
    public sealed record InvalidState(string Message) : SupplierRfqResult;
}
