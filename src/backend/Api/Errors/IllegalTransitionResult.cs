using System.Text.Json.Nodes;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;

namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// §3: "Illegal transitions return <c>409 Conflict</c>
/// (<c>type: …/errors/invalid-state-transition</c>) listing the current state and the allowed next
/// states", and §12.4 spells the code: <c>ILLEGAL_TRANSITION</c>, "includes <c>allowedNext</c>".
///
/// <para><b>This did not exist.</b> Every RFQ transition answered 400
/// <c>{ error: "invalid_state" }</c>, so a client could not tell "your payload is wrong" from "this
/// resource has moved on", and had nothing to reconcile against. T3-36 is what made the gap
/// load-bearing: adding three states changes what follows UnderEvaluation, and a caller that cannot
/// read the allowed set has no way to learn it.</para>
///
/// <para>State names are the enum's own, matching every other state on the wire (the DTOs already
/// emit <c>rfq.State.ToString()</c>) - not the display labels, which are localized and belong to
/// UX-WRITING §7.</para>
/// </summary>
/// <para><b>T-065: generalised rather than duplicated.</b> Proposal endpoints answered 400 for the
/// same situation, so one product had two conventions for "this resource has moved on" - and Phase 1
/// of batch 7 made the proposal machine something callers actually hit. Taking the state and its
/// allowed set as strings keeps ONE result type for every aggregate; a second copy parameterised on
/// ProposalState would be the second-anything this project keeps paying for.</para>
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
