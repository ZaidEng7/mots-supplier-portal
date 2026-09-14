// Puts each route's required permission into the published API document.
//
// The document is the source for the interface's types and for the finance-system client. Every route
// in this API is gated by a permission, and the published document said nothing about any of them, so a
// consumer reading it could not tell which token would be admitted where and learned the answer as a
// refusal at run time.
//
// The permissions are read off the route's own metadata, the same object the filter enforces, so a
// route that changes its permission changes its documentation in the same edit. A hand-kept list is
// the thing this project keeps finding wrong: it agrees with the code the day it is written and drifts
// silently afterwards. The precondition transformer does the same thing for the same reason.
//
// A set of permissions means any one of them, which is what the filter means by a set, so that a read
// several roles legitimately need can stay open while its writes stay narrow.
//
// The refusal response is documented alongside, because a permission a caller can read and a refusal
// they cannot anticipate is only half an answer. The permission is also named in the prose
// description, since a code generator that drops unknown extension fields still carries the words.

namespace MotsSupplierPortal.Api.Authorization;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

internal sealed class PermissionOpenApiTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var required = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<RequiredPermissionsMetadata>()
            .SelectMany(m => m.Permissions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (required.Count == 0) return Task.CompletedTask;

        var sentence = required.Count == 1
            ? $"Requires the `{required[0]}` permission."
            : $"Requires any one of: {string.Join(", ", required.Select(p => $"`{p}`"))}.";

        operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? sentence
            : $"{operation.Description}\n\n{sentence}";

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-required-permissions"] = new JsonNodeExtension(
            new System.Text.Json.Nodes.JsonArray([.. required.Select(p => System.Text.Json.Nodes.JsonValue.Create(p))]));

        operation.Responses ??= [];
        operation.Responses["403"] = new OpenApiResponse { Description = "FORBIDDEN" };

        return Task.CompletedTask;
    }
}
