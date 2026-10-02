// The people and access section of the administrator's dashboard: accounts, roles, sessions, invitations,
// lockouts and organisations.
//
// The frame around it, the status, the permission that hides it and the separate scope it runs in, is described
// in AdminDashboardContracts.cs. PeopleAndAccessSectionHandler computes every figure here and says exactly what
// each one counts.
//
//
// STAFF AND SUPPLIER ACCOUNTS ARE NEVER ADDED TOGETHER
//
// Every figure that concerns an account sits twice, once in Staff and once in Suppliers, and no field holds their
// sum. The registry's supplier logins outnumber the ministry's staff many times over, so a combined figure would
// describe neither, and a change on the staff side would vanish inside it.
//
// Staff are the accounts with no company attached, the same test the staff list uses, so a platform
// administrator, who belongs to no buying body either, is staff. Suppliers are the accounts with one.
//
// Active and inactive are counted apart. Every other figure in an account record is over active accounts only,
// because a deactivated account can do nothing and asks nothing of an administrator.
//
//
// ROLES ARE COUNTED PER ROLE, AND ALSO ONCE PER PERSON
//
// ActiveUsersByRole has one row for every role the platform has, a role nobody holds included, counting the active
// accounts that hold it. One person can hold two roles, so those rows cannot be added up to a number of people,
// and ActiveWithARole on each side is that number, each person counted once.
//
//
// CANNOT SIGN IN IS NOT A SUGGESTION TO RESET TWO-FACTOR
//
// CannotSignIn counts the active accounts holding a role the two-factor policy names, listed in
// TwoFactorRequiredRoles, that have no second factor set up. Sign-in refuses them before any session exists, and
// setting one up needs a session, so they cannot sign in and cannot fix it themselves. Resetting their second
// factor would change nothing, so a screen must not offer it as the remedy.
//
//
// THE OTHER FIGURES
//
// ActiveSessions counts sign-ins that are still open, by sign-in rather than by token, and
// PeopleWithActiveSessions the people holding at least one.
//
// PendingInvitations counts the people whose invitation link still works and has not been used: staff
// invitations on the staff side, a supplier's team invitations on the supplier side.
//
// StaffInvitedNeverSignedIn is staff only. A supplier's team invitation is recorded against the supplier rather
// than the person, so a supplier invitation can be followed only while its link lives, which PendingInvitations
// already does.
//
// LockedOut counts the accounts whose lockout has not yet run out.
//
// SupplierLoginsOnPlaceholderAddresses is information, never a fault: the active supplier logins the ERP import
// created on an address that cannot receive mail, because the ERP held none for that supplier.
//
// OrganisationsByType has one row for every type of buying body, with active and inactive apart.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Domain.Organizations;

public sealed record DashboardPeopleAndAccessDto(
    DashboardAccountsDto Staff,
    DashboardAccountsDto Suppliers,
    IReadOnlyList<DashboardRoleCountDto> ActiveUsersByRole,
    DashboardStaffInvitedNeverSignedInDto StaffInvitedNeverSignedIn,
    IReadOnlyList<string> TwoFactorRequiredRoles,
    int SupplierLoginsOnPlaceholderAddresses,
    IReadOnlyList<DashboardOrganisationTypeCountDto> OrganisationsByType);

public sealed record DashboardAccountsDto(
    int Active,
    int Inactive,
    int ActiveWithARole,
    int ActiveSessions,
    int PeopleWithActiveSessions,
    int PendingInvitations,
    int LockedOut,
    int CannotSignIn);

public sealed record DashboardRoleCountDto(string Role, int ActiveUsers);

public sealed record DashboardStaffInvitedNeverSignedInDto(int LinkStillValid, int LinkNoLongerValid);

public sealed record DashboardOrganisationTypeCountDto(OrganizationType Type, int Active, int Inactive);
