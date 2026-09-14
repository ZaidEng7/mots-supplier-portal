// A submission refused because the bid is incomplete, as distinct from one refused because of its
// state or the tender's window.
//
// The two used to share one answer. They should not: telling a supplier their bid has moved on says
// there is nothing to fix, while telling them the bid is incomplete says exactly what to go and fill
// in. A supplier with one unpriced line should get the second answer, not the first.
//
// Error is a lower_snake identifier, because that is what the middleware turns into the
// SCREAMING_SNAKE code the client reads. Only the missing-items one is named by the written interface
// contract; the other two are inventions, and they are marked as such where they are thrown.

namespace MotsSupplierPortal.Domain.Proposals;

public sealed class ProposalIncompleteException(string error, string message) : Exception(message)
{
    public string Error { get; } = error;
}
