using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MotsSupplierPortal.Api.Concurrency;

/// <summary>
/// Puts §8.1's precondition into the contract.
///
/// <para><b>The gap this closes.</b> <c>RequireIfMatch()</c> has refused unconditional writes since batch 3,
/// and the published OpenAPI document said nothing about it: no <c>If-Match</c> parameter, no 428, no ETag
/// response header anywhere. A client generated from that document could not write to this API at all, and
/// would discover why only at runtime - which is close to what happened to this repository's own SPA five
/// separate times in batch 13.</para>
///
/// <para>Driven off the endpoint metadata rather than a hand-kept list, so an endpoint that adds the filter
/// gets the documentation with it. <see cref="IfMatchPreconditionSweepTests"/> (integration) and the SPA's
/// own sweep both read the same two markers, one through the endpoint table and one through the published
/// document.</para>
/// </summary>
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
            // On the success response only. A 404 carries no version, and saying it does would send a
            // client looking for a header that is not there.
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
