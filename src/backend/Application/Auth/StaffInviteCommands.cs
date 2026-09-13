using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Auth;

/// <param name="OrganizationId">
/// The buying body this person works for, and null for the roles that deliberately have none.
///
/// <para>Added in batch 12 because there was no way to set it at all. Staff could only be created
/// through this invitation, the invitation never carried an organization, and BRULE-029 scopes every
/// tender query by one - so an invited procurement officer could sign in, see the tender list, press
/// New RFQ and get a bare 404 from a create that had nowhere to put the row. A clean installation
/// could not run a tender through its own interface.</para>
///
/// <para>Null is a real answer, not a missing one: <c>ministry_viewer</c> is unassigned on purpose,
/// because BRULE-086 grants the Ministry cross-organization access and pinning it to one buying body
/// would be a narrower grant wearing the same name. <c>system_admin</c> administers the platform
/// rather than procuring through it.</para>
/// </param>
public sealed record InviteStaffCommand(string Email, string FullName, string Role, Guid? OrganizationId);

public sealed record AcceptStaffInviteCommand(string Token, string Password);

public sealed record StaffAccountCommand(Guid UserId);

public sealed record ChangeStaffRoleCommand(Guid UserId, string Role);
