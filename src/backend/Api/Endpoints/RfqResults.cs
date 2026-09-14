// How a tender handler's answer becomes an HTTP response.
//
// Two mappings, because two kinds of caller ask. The buyer's covers the whole set of outcomes a buying
// officer or manager can produce. The supplier's covers the three a bidder can see.
//
// An illegal state change answers conflict, naming the current state and what may legally follow. Every
// tender transition answered a plain bad request before that was built.
//
// A deadline move the caller is not permitted to make in that direction is refused rather than answered
// not-found. The caller demonstrably can see this tender, because they reached here holding the edit
// permission on it, so hiding its existence protects nothing, and hiding the reason would leave an
// officer unable to tell a wrong direction from something broken.
//
// Naming an ineligible new owner is unprocessable. The payload is well-formed and names a real user; what
// is wrong is a fact about that user the client could not have known.
//
// A supplier who was not invited gets not-found and never a refusal, so the API never reveals that a
// tender exists.
//
// They are their own class rather than private members of the routes, so a route can say where its
// answer is decided and a reader looking for the status codes has one place to look.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Rfqs;

internal static class RfqResults
{
    internal static IResult MapMutation(RfqMutationResult result) => result switch
    {
        RfqMutationResult.Success s => Results.Ok(s.Rfq),
        RfqMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        RfqMutationResult.IllegalTransition illegal => IllegalTransitionResult.For(illegal.CurrentState, illegal.Message),
        RfqMutationResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        RfqMutationResult.DeadlineChangeNotPermitted =>
            Results.Json(new { error = "deadline_change_not_permitted" }, statusCode: StatusCodes.Status403Forbidden),

        RfqMutationResult.IneligibleUser ineligible =>
            Results.UnprocessableEntity(new { error = "ineligible_user", message = ineligible.Message }),

        RfqMutationResult.InvalidCategory => Results.BadRequest(new { error = "invalid_category" }),
        RfqMutationResult.InvalidUnitOfMeasure => Results.BadRequest(new { error = "invalid_unit_of_measure" }),
        RfqMutationResult.InvalidEvaluationTemplate invalid => Results.BadRequest(new { error = "invalid_evaluation_template", message = invalid.Message }),
        RfqMutationResult.SupplierNotActive => Results.BadRequest(new { error = "supplier_not_active" }),
        _ => Results.Problem(),
    };

    internal static IResult MapSupplierResult(SupplierRfqResult result) => result switch
    {
        SupplierRfqResult.Success s => Results.Ok(s.Rfq),
        SupplierRfqResult.NotFoundOrNotInvited => Results.NotFound(),
        SupplierRfqResult.InvalidState invalid => Results.BadRequest(new { error = "invalid_state", message = invalid.Message }),
        _ => Results.Problem(),
    };
}
