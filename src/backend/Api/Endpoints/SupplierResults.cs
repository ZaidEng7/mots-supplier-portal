// How a supplier-profile handler's answer becomes an HTTP response, and the one refusal that carries a
// version back with it.
//
// Two mappings. The profile patch has an outcome the others do not, which is why it has its own.
//
// A field the caller is not currently permitted to edit is refused rather than answered as a conflict,
// because that is a permission outcome rather than a clash of state.
//
//
// THE STALE-WRITE REFUSAL
//
// A lost update is a failed precondition rather than a conflict. This route used to answer conflict with
// the current version in the body, which predates the current contract; conflict now means only what the
// contract says it means.
//
// The winner's version travels back on the header rather than in the body, because that is where a client
// looking to re-read and retry is already looking.
//
// It is its own result type because this is the one handler that detects staleness itself rather than
// letting the data layer throw, so the pipeline's own translation never sees it.
//
// These were local functions inside the route-mapping method, which put two switch statements in the
// middle of a list of routes. They are a class of their own now, so a route can say where its answer is
// decided.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Suppliers;

internal static class SupplierResults
{
    internal static IResult MapProfileResult(UpdateProfileResult result) => result switch
    {
        UpdateProfileResult.Success s => Results.Ok(s.Supplier),
        UpdateProfileResult.NotFoundOrOutOfScope => Results.NotFound(),
        UpdateProfileResult.Conflict c => new StaleVersionResult(c.CurrentRowVersion),
        UpdateProfileResult.NotEditable n => Results.Json(
            new { error = "field_not_flagged", detail = n.Reason },
            statusCode: StatusCodes.Status403Forbidden),
        UpdateProfileResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
        _ => Results.Problem(),
    };

    internal static IResult MapMutation(ProfileMutationResult result) => result switch
    {
        ProfileMutationResult.Success s => Results.Ok(s.Supplier),
        ProfileMutationResult.NotFoundOrOutOfScope => Results.NotFound(),
        ProfileMutationResult.NotEditable n => Results.Json(
            new { error = "field_not_flagged", detail = n.Reason },
            statusCode: StatusCodes.Status403Forbidden),
        ProfileMutationResult.InvalidState i => Results.Conflict(new { error = i.Reason }),
        _ => Results.Problem(),
    };
}

internal sealed record StaleVersionResult(uint CurrentRowVersion) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.SetETag(CurrentRowVersion);
        return ProblemResponse.WriteAsync(httpContext, ProblemResponse.Build(
        httpContext, StatusCodes.Status412PreconditionFailed, ProblemTypes.PreconditionFailed,
        "The precondition failed.", "ETAG_MISMATCH",
        "This resource changed after you loaded it. Refetch it and reapply your change."));
    }
}
