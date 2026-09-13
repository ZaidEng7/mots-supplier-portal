// The refusal a caller gets when they try a state change the resource does not allow: a 409, naming
// the current state and every state that could legally come next.
//
// This did not exist. Every tender transition answered a plain 400 saying the state was invalid, so a
// client could not tell "your payload is wrong" from "this resource has moved on", and had nothing to
// reconcile against. Adding three states to the tender lifecycle is what made the gap matter: it
// changes what follows evaluation, and a caller who cannot read the allowed set has no way to learn
// it.
//
// It is one result type for every kind of resource rather than one per resource. Bids used to answer
// 400 for the same situation, so one product had two conventions for "this has moved on". Taking the
// state and its allowed set as plain text keeps a single type; a second copy typed to bids would be
// the second-of-everything this project keeps paying for.
//
// The state names are the code's own names, matching every other state on the wire. They are not the
// display labels, which are translated and belong to the interface.

namespace MotsSupplierPortal.Api.Errors;

using System.Text.Json.Nodes;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;

internal sealed record IllegalTransitionResult(string CurrentState, IReadOnlyList<string> AllowedNext, string Message) : IResult
{
    public static IllegalTransitionResult For(RfqState state, string message) =>
        new(state.ToString(), [.. Rfq.AllowedNextFrom(state).Select(n => n.ToString())], message);

    public static IllegalTransitionResult For(ProposalState state, string message) =>
        new(state.ToString(), [.. Proposal.AllowedNextFrom(state).Select(n => n.ToString())], message);

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = ProblemResponse.Build(
            httpContext, StatusCodes.Status409Conflict, ProblemTypes.InvalidStateTransition,
            "The requested transition is not allowed from the current state.",
            code: "ILLEGAL_TRANSITION", detail: Message);

        problem["currentState"] = CurrentState;
        problem["allowedNext"] = new JsonArray([.. AllowedNext.Select(next => JsonValue.Create(next))]);

        return ProblemResponse.WriteAsync(httpContext, problem);
    }
}
