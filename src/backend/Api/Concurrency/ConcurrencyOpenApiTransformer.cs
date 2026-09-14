// Puts the write precondition into the published API document.
//
// A write on an existing resource must send the version it read, or it is refused. The document said
// nothing about that, so a consumer generating a client from it produced one that could not perform a
// single update, and learned why only by running it.
//
// Which routes require it is read off the route's own marker, the same one the filter sets, so a route
// that starts or stops requiring a precondition changes its documentation in the same edit. The
// permission transformer does the same thing for the same reason.
//
// The two refusals are documented alongside the header: one for a missing precondition and one for a
// failed or unreadable one. A required header a caller can read and refusals they cannot anticipate is
// only half an answer.

namespace MotsSupplierPortal.Api.Concurrency;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

internal sealed class ConcurrencyOpenApiTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<RequiresIfMatchMetadata>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match",
                In = ParameterLocation.Header,
                Required = true,
                Description = "The ETag of the version being edited, from a prior read of this resource "
                    + "or of the aggregate containing it. Missing: 428. Not a version this API issued, "
                    + "or no longer current: 412.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });

            operation.Responses ??= [];
            operation.Responses["412"] = new OpenApiResponse { Description = "ETAG_MISMATCH" };
            operation.Responses["428"] = new OpenApiResponse { Description = "IF_MATCH_REQUIRED" };
        }

        if (metadata.OfType<EmitsETagMetadata>().Any())
        {
            var header = new OpenApiHeader
            {
                Description = "The version of this resource, to be sent back as If-Match on a write.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };

            foreach (var (status, response) in operation.Responses ?? [])
            {
                if (!status.StartsWith('2') || response is not OpenApiResponse concrete) continue;

                concrete.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
                concrete.Headers["ETag"] = header;
            }
        }

        return Task.CompletedTask;
    }
}
