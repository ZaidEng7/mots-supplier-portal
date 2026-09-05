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

public sealed record ChangeStaffRoleRequest(string Role);

public sealed class ChangeStaffRoleRequestValidator : AbstractValidator<ChangeStaffRoleRequest>
{
    public ChangeStaffRoleRequestValidator() => RuleFor(x => x.Role).NotEmpty();
}

/// <summary>Task #28/FR-ADM-001. Mirrors SupplierUserEndpoints's shape exactly.</summary>
public static class StaffEndpoints
{
    private static IResult Map(StaffAccountResult result) => result switch
    {
        StaffAccountResult.Success s => Results.Ok(s.Staff),
        // §9.2: a user who is not a staff account - a supplier's user, or nobody - is a 404 rather than a
        // 403. There is no information in the difference that an administrator needs and an attacker does
        // not.
        StaffAccountResult.NotFound => Results.NotFound(),
        StaffAccountResult.CannotActOnSelf =>
            Results.UnprocessableEntity(new { error = "cannot_act_on_own_account" }),
        StaffAccountResult.WouldLockOutAdministration =>
            Results.UnprocessableEntity(new { error = "would_lock_out_administration" }),
        _ => Results.Problem(),
    };

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

        // ─── T-077: administering an account, not merely creating one ────────────────────────────
        //
        // SCR-701/SCR-702, both P0, had no screen and no endpoint. `system_admin` could invite a staff
        // account and then never list, deactivate, re-role or MFA-reset one - so an account created in
        // error could not be removed at all.
        var admin = app.MapGroup("/api/v1/staff").WithTags("Staff");

        admin.MapGet("/", async (string? cursor, int? pageSize, string? withCount,
            IListStaffHandler handler, CancellationToken ct) =>
        {
            // The same ?withCount= handling every other list uses. A second parser here would be a
            // second answer to the same question - see SupplierUserEndpoints, which this mirrors.
            if (!FilterValues.TryParseBoolFilter(withCount, out _, out var badWithCount))
            {
                return FilterValues.InvalidFilterValue("withCount", badWithCount!);
            }

            return Results.Ok(await handler.HandleAsync(cursor, pageSize, FilterValues.BoolOrFalse(withCount), ct));
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ListStaff");

        admin.MapPost("/{userId:guid}/deactivate", async (Guid userId, ISetStaffActiveHandler handler, CancellationToken ct) =>
            Map(await handler.HandleAsync(userId, isActive: false, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("DeactivateStaff");

        admin.MapPost("/{userId:guid}/reactivate", async (Guid userId, ISetStaffActiveHandler handler, CancellationToken ct) =>
            Map(await handler.HandleAsync(userId, isActive: true, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ReactivateStaff");

        admin.MapPut("/{userId:guid}/role", async (
            Guid userId, ChangeStaffRoleRequest request, IValidator<ChangeStaffRoleRequest> validator,
            IChangeStaffRoleHandler handler, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return ValidationProblems.From(validation);

            return Map(await handler.HandleAsync(new ChangeStaffRoleCommand(userId, request.Role), ct));
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ChangeStaffRole");

        // A reset of someone ELSE's second factor. `system_admin` cannot hold a session without MFA, so
        // a lost authenticator is otherwise a lockout with no path back; and a self-service reset would
        // be a way past the factor itself, which is why the handler refuses one.
        admin.MapPost("/{userId:guid}/reset-mfa", async (Guid userId, IResetStaffMfaHandler handler, CancellationToken ct) =>
            Map(await handler.HandleAsync(userId, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ResetStaffMfa");

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
