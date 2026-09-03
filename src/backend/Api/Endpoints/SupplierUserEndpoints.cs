using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record InviteSupplierUserRequest(string Email, string FullName);

public sealed class InviteSupplierUserRequestValidator : AbstractValidator<InviteSupplierUserRequest>
{
    public InviteSupplierUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
    }
}

public sealed record AcceptSupplierUserInviteRequest(string Token, string Password);

public sealed class AcceptSupplierUserInviteRequestValidator : AbstractValidator<AcceptSupplierUserInviteRequest>
{
    public AcceptSupplierUserInviteRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

/// <summary>FEAT-04.8/FR-PROF-008/MSP-55.</summary>
public static class SupplierUserEndpoints
{
    public static void MapSupplierUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers/me/users").WithTags("SupplierUsers");

        group.MapGet("/", async (string? cursor, int? pageSize, bool? withCount, HttpContext httpContext, IListSupplierUsersHandler handler, CancellationToken ct) =>
            ListResponse.Ok(httpContext, await handler.HandleAsync(cursor, pageSize, withCount == true, ct), pageSize))
        .RequirePermission(Permissions.SupplierUserManage)
        // Alphabetical by email: a team list is read to find a person, not to see what changed last.
        .WithListQuery(ListQueryPolicy.Create("email", ["email"]))
        .WithName("ListSupplierUsers");

        group.MapPost("/", async (
            InviteSupplierUserRequest request,
            IValidator<InviteSupplierUserRequest> validator,
            IInviteSupplierUserHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new InviteSupplierUserCommand(request.Email, request.FullName), ct);
            return result switch
            {
                InviteSupplierUserResult.Success s => Results.Created($"/api/v1/suppliers/me/users/{s.User.UserId}", s.User),
                InviteSupplierUserResult.DuplicateEmail => Results.Conflict(new { error = "duplicate_email" }),
                InviteSupplierUserResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierUserManage)
        .WithName("InviteSupplierUser");

        group.MapPost("/{userId:guid}/disable", async (Guid userId, IDisableSupplierUserHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DisableSupplierUserCommand(userId), ct);
            return result switch
            {
                DisableSupplierUserResult.Success => Results.NoContent(),
                DisableSupplierUserResult.NotFoundOrOutOfScope => Results.NotFound(),
                _ => Results.Problem(),
            };
        })
        .RequirePermission(Permissions.SupplierUserManage)
        .WithName("DisableSupplierUser");

        app.MapPost("/api/v1/supplier-users/accept-invite", async (
            AcceptSupplierUserInviteRequest request,
            IValidator<AcceptSupplierUserInviteRequest> validator,
            IAcceptSupplierUserInviteHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new AcceptSupplierUserInviteCommand(request.Token, request.Password), ct);
            return result switch
            {
                AcceptSupplierUserInviteResult.Success => Results.Ok(new { accepted = true }),
                AcceptSupplierUserInviteResult.InvalidOrExpiredToken => Results.BadRequest(new { error = "invalid_or_expired_token" }),
                AcceptSupplierUserInviteResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithTags("SupplierUsers")
        .WithName("AcceptSupplierUserInvite")
        .RequireRateLimiting("auth-strict")
        // Public by design: the invitee has no session yet - the invite token is the credential.
        .AllowAnonymous();
    }
}
