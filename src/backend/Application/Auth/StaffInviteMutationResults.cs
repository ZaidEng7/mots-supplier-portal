using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Auth;

public abstract record InviteStaffResult
{
    public sealed record Success(StaffDto Staff) : InviteStaffResult;
    public sealed record DuplicateEmail : InviteStaffResult;
    /// <summary>Role is not one of the invitable staff roles - see InviteStaffHandler for the
    /// list (supplier_admin/supplier_user are deliberately excluded; those come from supplier
    /// registration/the supplier-side invite, not this endpoint).</summary>
    public sealed record InvalidRole : InviteStaffResult;
}

public abstract record AcceptStaffInviteResult
{
    public sealed record Success : AcceptStaffInviteResult;
    public sealed record InvalidOrExpiredToken : AcceptStaffInviteResult;
    public sealed record WeakPassword(IReadOnlyList<string> Errors) : AcceptStaffInviteResult;
}

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
