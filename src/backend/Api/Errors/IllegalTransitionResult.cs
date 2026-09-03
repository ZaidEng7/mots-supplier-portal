using System.Text.Json.Nodes;
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
internal sealed record IllegalTransitionResult(RfqState CurrentState, string Message) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        var problem = ProblemResponse.Build(
            httpContext, StatusCodes.Status409Conflict, ProblemTypes.InvalidStateTransition,
            "The requested transition is not allowed from the current state.",
            code: "ILLEGAL_TRANSITION", detail: Message);

        problem["currentState"] = CurrentState.ToString();
        problem["allowedNext"] = new JsonArray(
            [.. Rfq.AllowedNextFrom(CurrentState).Select(next => JsonValue.Create(next.ToString()))]);

        return ProblemResponse.WriteAsync(httpContext, problem);
    }
}
