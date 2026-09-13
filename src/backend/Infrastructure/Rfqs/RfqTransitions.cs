// Deciding which of the two refusals a domain objection is.
//
// The written contract says an illegal transition answers with a conflict naming the current state and the
// allowed next ones. Everything else a domain guard refuses, a missing item, an unbound template, an
// inconsistent timeline, is a bad request about the request.
//
//
// DECIDED FROM THE MAP OF LEGAL MOVES, NOT FROM THE EXCEPTION
//
// Two designs were tried first and both were worse.
//
// A narrower exception type broke the architecture rule that domain exceptions must be sealed or abstract,
// and rightly so: that rule exists so nobody grows an exception hierarchy nobody can see.
//
// Matching on the message text would make an HTTP status depend on wording.
//
// Asking the aggregate's own map whether the move this handler ATTEMPTED is legal from where the aggregate
// actually is answers the question directly. If it was legal, the refusal came from some other invariant and
// is a bad request. If it was not, that is precisely an illegal transition.
//
// An operation that is not a transition at all, editing content or adding an item, passes no target, because
// a non-transition can never be an illegal transition.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

internal static class RfqTransitions
{
    public static RfqMutationResult Refusal(Rfq rfq, DomainException ex, RfqState? target = null)
    {
        if (target is not { } wanted || Rfq.AllowedNextFrom(rfq.State).Contains(wanted))
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        return new RfqMutationResult.IllegalTransition(rfq.State, ex.Message);
    }
}
