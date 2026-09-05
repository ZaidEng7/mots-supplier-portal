using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Auth;

public sealed record StaffDto(Guid UserId, string Email, string FullName, string Role);

public sealed record InviteStaffCommand(string Email, string FullName, string Role);

public abstract record InviteStaffResult
{
    public sealed record Success(StaffDto Staff) : InviteStaffResult;
    public sealed record DuplicateEmail : InviteStaffResult;
    /// <summary>Role is not one of the invitable staff roles - see InviteStaffHandler for the
    /// list (supplier_admin/supplier_user are deliberately excluded; those come from supplier
    /// registration/the supplier-side invite, not this endpoint).</summary>
    public sealed record InvalidRole : InviteStaffResult;
}

public interface IInviteStaffHandler
{
    Task<InviteStaffResult> HandleAsync(InviteStaffCommand command, CancellationToken ct);
}

public sealed record AcceptStaffInviteCommand(string Token, string Password);

public abstract record AcceptStaffInviteResult
{
    public sealed record Success : AcceptStaffInviteResult;
    public sealed record InvalidOrExpiredToken : AcceptStaffInviteResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : AcceptStaffInviteResult;
}

public interface IAcceptStaffInviteHandler
{
    Task<AcceptStaffInviteResult> HandleAsync(AcceptStaffInviteCommand command, CancellationToken ct);
}

// ─── T-077: administering a staff account, not merely creating one ────────────────────────────────
//
// SCR-701 and SCR-702 are both P0 in SCREEN-INVENTORY and neither had a screen OR an endpoint: the
// only staff routes were invite and accept-invite. `system_admin` could create an account and then
// never list, inspect, deactivate or reset MFA for one - so an account created in error could not be
// removed, which is the half of this that is a security gap rather than an inconvenience.

/// <summary>One staff account as an administrator sees it. No supplier fields: an account created
/// through the staff invite never carries a SupplierId (see InviteStaffHandler).</summary>
public sealed record StaffAccountDto(
    Guid UserId, string Email, string FullName, string? Role, bool IsActive,
    /// <summary>Whether MFA is currently enrolled. `system_admin` requires it to hold a session, so an
    /// administrator locked out of their authenticator is a real support case - see ResetStaffMfa.</summary>
    bool MfaEnabled,
    DateTimeOffset? LockoutEnd,
    int ActiveSessionCount);

public interface IListStaffHandler
{
    /// <summary>Keyset-paged on (email, id), the same shape as the supplier-user list - MSP-84's
    /// reasoning applies identically.</summary>
    Task<ListEnvelope<StaffAccountDto>> HandleAsync(string? cursor, int? limit, bool withCount, CancellationToken ct);
}

public sealed record StaffAccountCommand(Guid UserId);

public abstract record StaffAccountResult
{
    public sealed record Success(StaffAccountDto Staff) : StaffAccountResult;
    public sealed record NotFound : StaffAccountResult;

    /// <summary>
    /// The action would leave the platform with no way back in.
    ///
    /// <para>Deactivating the last active `system_admin` is refused for the same reason
    /// UpdateRolePermissions refuses to remove the last `admin.roles.manage`: the recovery path
    /// afterwards is a database write by hand, and a product that can lock every administrator out of
    /// itself through its own UI has a defect, not a policy.</para>
    /// </summary>
    public sealed record WouldLockOutAdministration : StaffAccountResult;

    /// <summary>Acting on your own account, where doing so is what makes it dangerous.</summary>
    public sealed record CannotActOnSelf : StaffAccountResult;
}

public interface ISetStaffActiveHandler
{
    Task<StaffAccountResult> HandleAsync(Guid userId, bool isActive, CancellationToken ct);
}

public sealed record ChangeStaffRoleCommand(Guid UserId, string Role);

public interface IChangeStaffRoleHandler
{
    Task<StaffAccountResult> HandleAsync(ChangeStaffRoleCommand command, CancellationToken ct);
}

public interface IResetStaffMfaHandler
{
    /// <summary>Clears the authenticator enrolment so the holder re-enrols on next sign-in, and kills
    /// every live session - a reset that left the old sessions alive would hand an attacker who already
    /// has one a way to stay.</summary>
    Task<StaffAccountResult> HandleAsync(Guid userId, CancellationToken ct);
}
