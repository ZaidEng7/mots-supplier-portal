// What the staff-administration writes can answer.
//
// A role that is not one of the invitable staff roles is refused. The two supplier roles are deliberately
// excluded, because those accounts come from a supplier registering or from the supplier's own invitation
// rather than from here.
//
//
// THE LAST-ADMINISTRATOR REFUSAL
//
// Deactivating the last active system administrator is refused, for the same reason editing roles refuses to
// remove the last permission that can edit roles.
//
// The recovery path afterwards is a database write by hand, and a product that can lock every administrator
// out of itself through its own interface has a defect rather than a policy.

namespace MotsSupplierPortal.Application.Auth;

using MotsSupplierPortal.Application.Common;

public abstract record InviteStaffResult
{
    public sealed record Success(StaffDto Staff) : InviteStaffResult;
    public sealed record DuplicateEmail : InviteStaffResult;
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

    public sealed record WouldLockOutAdministration : StaffAccountResult;

    public sealed record CannotActOnSelf : StaffAccountResult;
}
