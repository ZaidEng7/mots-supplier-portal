// Managing the credentials other systems use: /api/v1/admin/api-keys.
//
// THE SECRET IS IN THE CREATE RESPONSE AND NOWHERE ELSE. There is no route that reads it back, because nothing
// stores it - the table holds a hash. That is the property worth protecting: a credential that can be looked up
// again is one that can be looked up by anyone who reaches the route, and one an administrator will assume they
// can recover instead of replacing.
//
// THERE IS NO EDIT ROUTE. A key's permissions and expiry are what they were at issue. Widening a live
// credential in place would leave an audit trail that says a key was created with one reach and no record of it
// gaining another; issuing a second key says the same thing honestly and can be revoked on its own.
//
// REVOKE IS A POST AND NOT A DELETE, because the row survives. The audit trail names keys by prefix and a
// prefix that resolves to nothing makes a year-old row unreadable at exactly the moment somebody is asking who
// read the registry.
//
// ONE PERMISSION GUARDS ALL THREE, admin.apiKeys.manage, held by system_admin alone - which is the role that
// requires a second factor. That friction is the point here rather than an obstacle: minting a credential that
// reads the national supplier registry is the operation where being sure who is asking matters most.
//
// THE LIFETIME IS BOUNDED AT BOTH ENDS. A day is the shortest useful key and five years the longest defensible
// one; the default when the caller says nothing is a year. A validator refusing a nonsense number is better than
// a key that expired yesterday or in 2085.

namespace MotsSupplierPortal.Api.Endpoints;

using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Errors;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;

public sealed record CreateApiKeyRequest(string Name, int? LifetimeDays);

public sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LifetimeDays).InclusiveBetween(1, 1825).When(x => x.LifetimeDays is not null);
    }
}

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/api-keys").WithTags("ApiKeys");

        group.MapGet("/", async (IListApiKeysHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminApiKeysManage)
        .WithName("ListApiKeys");

        group.MapPost("/", async (
            CreateApiKeyRequest request,
            ICreateApiKeyHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CreateApiKeyCommand(request.Name, request.LifetimeDays), ct);

            return result is ApiKeyMutationResult.Created created
                ? Results.Created($"/api/v1/admin/api-keys/{created.Key.Key.Id}", created.Key)
                : Results.NotFound();
        })
        .RequirePermission(Permissions.AdminApiKeysManage)
        .Validate<CreateApiKeyRequest>()
        .WithName("CreateApiKey");

        group.MapPost("/{apiKeyId:guid}/revoke", async (
            Guid apiKeyId,
            IRevokeApiKeyHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RevokeApiKeyCommand(apiKeyId), ct);

            return result switch
            {
                ApiKeyMutationResult.Revoked revoked => Results.Ok(revoked.Key),
                _ => Results.NotFound(),
            };
        })
        .RequirePermission(Permissions.AdminApiKeysManage)
        .WithName("RevokeApiKey");
    }
}
