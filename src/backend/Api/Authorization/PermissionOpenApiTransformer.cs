using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MotsSupplierPortal.Api.Authorization;

/// <summary>
/// T-108: puts each operation's REQUIRED PERMISSION into the contract.
///
/// <para><b>The gap this closes.</b> §11 says the OpenAPI document is the source for the SPA's types
/// and for the ERP client. Every route in this API is gated by a permission - <c>PermissionEndpointFilter</c>
/// has refused callers since batch 2 - and the published document said nothing about any of them. A
/// consumer reading it could not tell which token would be admitted to which route, and learned the
/// answer as a 403 at runtime.</para>
///
/// <para><b>Derived, never hand-typed.</b> The permissions come off the endpoint's own
/// <see cref="RequiredPermissionsMetadata"/>, the same object the filter enforces, so a route that
/// changes its permission changes its documentation in the same edit. A hand-kept list is the thing
/// this repository keeps finding wrong: it agrees with the code on the day it is written and drifts
/// silently afterwards. Same shape and same reason as
/// <c>ConcurrencyOpenApiTransformer</c>, which does this for §8.1's precondition.</para>
///
/// <para><b>The 403 is documented with it</b>, because a permission a caller can read and a refusal
/// they cannot anticipate is only half an answer. The description names the permission in words as
/// well, since a generator that drops unknown <c>x-</c> extensions still carries the prose.</para>
/// </summary>
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

        // "Any one of these", which is what the filter means by a set - RequireAnyPermission exists so a
        // read several roles legitimately need can stay open while its writes stay narrow.
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
