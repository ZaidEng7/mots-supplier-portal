using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

public sealed record InviteStaffRequest(string Email, string FullName, string Role);

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public sealed record AcceptStaffInviteRequest(string Token, string Password);

public sealed class AcceptStaffInviteRequestValidator : AbstractValidator<AcceptStaffInviteRequest>
{
    public AcceptStaffInviteRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

/// <summary>Task #28/FR-ADM-001. Mirrors SupplierUserEndpoints's shape exactly.</summary>
public static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/staff/invite", async (
            InviteStaffRequest request,
            IValidator<InviteStaffRequest> validator,
            IInviteStaffHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new InviteStaffCommand(request.Email, request.FullName, request.Role), ct);
            return result switch
            {
                InviteStaffResult.Success s => Results.Created($"/api/v1/staff/{s.Staff.UserId}", s.Staff),
                InviteStaffResult.DuplicateEmail => Results.Conflict(new { error = "duplicate_email" }),
                InviteStaffResult.InvalidRole => Results.BadRequest(new { error = "invalid_role" }),
                _ => Results.Problem(),
            };
        })
        .WithTags("Staff")
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("InviteStaff");

        app.MapPost("/api/v1/staff/accept-invite", async (
            AcceptStaffInviteRequest request,
            IValidator<AcceptStaffInviteRequest> validator,
            IAcceptStaffInviteHandler handler,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            var result = await handler.HandleAsync(new AcceptStaffInviteCommand(request.Token, request.Password), ct);
            return result switch
            {
                AcceptStaffInviteResult.Success => Results.Ok(new { accepted = true }),
                AcceptStaffInviteResult.InvalidOrExpiredToken => Results.BadRequest(new { error = "invalid_or_expired_token" }),
                AcceptStaffInviteResult.WeakPassword w => Results.BadRequest(new { error = "weak_password", details = w.Errors }),
                _ => Results.Problem(),
            };
        })
        .WithTags("Staff")
        .WithName("AcceptStaffInvite")
        .RequireRateLimiting("auth-strict")
        // Public by design: the invitee has no session yet - the invite token is the credential.
        .AllowAnonymous();
    }
}
