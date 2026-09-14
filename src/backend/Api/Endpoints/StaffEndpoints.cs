// Administering staff accounts: inviting one, listing them, deactivating, changing a role, resetting somebody's
// second factor, and accepting an invitation.
//
// Two of the screens here had no route at all for a while. A system administrator could invite a staff account
// and then never list, deactivate, re-role or reset one, so an account created in error could not be removed.
//
// The organization on an invitation is optional and carries an explicit default rather than only being
// nullable, because the two are different on the wire. Without the default the generator emits it as required,
// and the contract check correctly refused that: a field that was optional and becomes required breaks every
// existing client that does not send it, even though nothing was removed.
//
// Two roles must be given an organization, and the rest must not.
//
// Every procurement query is scoped by the caller's organization, so an officer or a manager invited without
// one signs in successfully, holds every permission their role grants, and meets an empty product. No tenders,
// no dashboard figures, no approval queue, and no error anywhere to say why, because returning nothing is the
// correct answer to a request for the tenders of no organization.
//
// The other five roles are deliberately exempt. An evaluator is scoped by assignment and may belong to no
// organization at all. A reviewer works the national supplier registry, which belongs to no buying body. The
// ministry's viewer is cross-organization by rule, and pinning it to one would narrow it. A system
// administrator has no tenancy. Requiring an organization of any of those would refuse a legitimate invitation.
//
// A user who is not a staff account, whether a supplier's user or nobody at all, answers not-found rather than
// refused. There is no information in that difference which an administrator needs and an attacker does not.
//
// The count flag uses the same shared parsing every other list uses. A second parser here would be a second
// answer to the same question.
//
// Resetting somebody else's second factor exists because a system administrator cannot hold a session without
// one, so a lost authenticator would otherwise be a lockout with no way back. A self-service reset would be a
// way past the factor itself, which is why the handler refuses one.
//
// Accepting an invitation is public by design. The invitee has no session yet, and the invitation token is the
// credential.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Errors;
using FluentValidation;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;

public sealed record InviteStaffRequest(string Email, string FullName, string Role, Guid? OrganizationId = null);

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    private static readonly string[] RequireAnOrganization = [Roles.ProcurementOfficer, Roles.ProcurementManager];

    public InviteStaffRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty();

        RuleFor(x => x.OrganizationId)
            .NotNull()
            .When(x => x.Role is not null && RequireAnOrganization.Contains(x.Role, StringComparer.Ordinal))
            .WithMessage(x =>
                $"A '{x.Role}' works within one buying body, and every screen they have is scoped to it. "
                + "An invitation without an organization produces an account that signs in to an empty product.");
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

public static class StaffEndpoints
{
    private static IResult Map(StaffAccountResult result) => result switch
    {
        StaffAccountResult.Success s => Results.Ok(s.Staff),
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
            IInviteStaffHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new InviteStaffCommand(request.Email, request.FullName, request.Role, request.OrganizationId), ct);
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
        .Validate<InviteStaffRequest>()
        .WithName("InviteStaff");

        var admin = app.MapGroup("/api/v1/staff").WithTags("Staff");

        admin.MapGet("/", async (string? cursor, int? pageSize, string? withCount,
            IListStaffHandler handler, CancellationToken ct) =>
        {
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
            Guid userId, ChangeStaffRoleRequest request, 
            IChangeStaffRoleHandler handler, CancellationToken ct) =>
        {
            return Map(await handler.HandleAsync(new ChangeStaffRoleCommand(userId, request.Role), ct));
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .Validate<ChangeStaffRoleRequest>()
        .WithName("ChangeStaffRole");

        admin.MapPost("/{userId:guid}/reset-mfa", async (Guid userId, IResetStaffMfaHandler handler, CancellationToken ct) =>
            Map(await handler.HandleAsync(userId, ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("ResetStaffMfa");

        app.MapPost("/api/v1/staff/accept-invite", async (
            AcceptStaffInviteRequest request,
            IAcceptStaffInviteHandler handler,
            CancellationToken ct) =>
        {
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
        .AllowAnonymous()
        .Validate<AcceptStaffInviteRequest>();
    }
}
