// What an administrator may ask about staff accounts: invite one, accept an invitation, deactivate, change a
// role, and reset somebody's second factor.
//
//
// THE ORGANIZATION ON AN INVITATION
//
// It is the buying body this person works for, and it is absent for the roles that deliberately have none.
//
// It was added because there was no way to set it at all. Staff could only be created through this
// invitation, the invitation never carried an organization, and every tender query is scoped by one.
//
// So an invited officer could sign in, see the tender list, press the button to create a tender, and get a
// bare not-found from a create that had nowhere to put the row. A clean installation could not run a tender
// through its own interface.
//
// Absent is a real answer rather than a missing one. The ministry's viewer is unassigned on purpose, because
// that grant is cross-organization and pinning it to one buying body would be a narrower grant wearing the
// same name. A system administrator administers the platform rather than procuring through it.

namespace MotsSupplierPortal.Application.Auth;

using MotsSupplierPortal.Application.Common;

public sealed record InviteStaffCommand(string Email, string FullName, string Role, Guid? OrganizationId);

public sealed record AcceptStaffInviteCommand(string Token, string Password);

public sealed record ChangeStaffRoleCommand(Guid UserId, string Role);
