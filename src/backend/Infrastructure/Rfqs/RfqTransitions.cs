using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>
/// Decides which of §3's two refusals a domain exception is.
///
/// <para>§3: "Illegal transitions return <c>409 Conflict</c> … listing the current state and the
/// allowed next states." Everything else a domain guard refuses - a missing item, an unbound
/// template, an inconsistent timeline - is a 400 about the request.</para>
///
/// <para><b>Decided from the legal-move map, not from the exception.</b> Two designs were tried
/// first and both were worse: a narrower exception type broke the architecture rule that domain
/// exceptions must be sealed or abstract (rightly - it exists so nobody grows an exception
/// hierarchy nobody can see), and matching on the message text would make an HTTP status depend on
/// wording. Asking <see cref="Rfq.AllowedNextFrom"/> whether the move this handler ATTEMPTED was
/// legal from where the aggregate actually is answers the question directly: if it was legal, the
/// refusal came from some other invariant and is a 400; if it was not, that is precisely an illegal
/// transition.</para>
/// </summary>
internal static class RfqTransitions
{
    /// <param name="target">
    /// The state the caller was trying to reach, or null when the operation is not a transition at
    /// all - editing content, adding an item. A non-transition can never be an illegal transition.
    /// </param>
    public static RfqMutationResult Refusal(Rfq rfq, DomainException ex, RfqState? target = null)
    {
        if (target is not { } wanted || Rfq.AllowedNextFrom(rfq.State).Contains(wanted))
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        return new RfqMutationResult.IllegalTransition(rfq.State, ex.Message);
    }
}
