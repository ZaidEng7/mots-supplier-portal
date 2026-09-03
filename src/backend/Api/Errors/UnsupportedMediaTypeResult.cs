using MotsSupplierPortal.Api.Errors;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// §7.1's <c>/errors/unsupported-media-type</c> (415, <c>MIME_NOT_ALLOWED</c>), used by §12.5's
/// PATCH to refuse anything that is not <c>application/merge-patch+json</c>.
///
/// <para>Refusing rather than accepting <c>application/json</c> is the whole point: RFC 7396's
/// absent-versus-null distinction is what the endpoint acts on, and a caller sending plain JSON has
/// not said which semantics it means.</para>
/// </summary>
internal sealed class UnsupportedMediaTypeResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        ProblemResponse.WriteAsync(httpContext, ProblemResponse.Build(
            httpContext, StatusCodes.Status415UnsupportedMediaType, ProblemTypes.UnsupportedMediaType,
            "Unsupported media type.", "MIME_NOT_ALLOWED",
            "This endpoint accepts application/merge-patch+json (RFC 7396)."));
}
