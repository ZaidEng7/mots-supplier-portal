// The refusal a caller gets when they send a partial update in the wrong format.
//
// The partial-update endpoints accept only the merge-patch content type. Refusing plain JSON rather
// than accepting it is the whole point: the endpoint acts on the difference between a field that was
// omitted and a field that was explicitly set to nothing, and a caller sending plain JSON has not said
// which of those they mean.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Errors;

internal sealed class UnsupportedMediaTypeResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        ProblemResponse.WriteAsync(httpContext, ProblemResponse.Build(
            httpContext, StatusCodes.Status415UnsupportedMediaType, ProblemTypes.UnsupportedMediaType,
            "Unsupported media type.", "MIME_NOT_ALLOWED",
            "This endpoint accepts application/merge-patch+json (RFC 7396)."));
}
