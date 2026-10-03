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
// already does. It is split by what became of the link: still valid, expired unused, not sent yet, or used. A used
// link with no sign-in after it is a person who set a password and never came back, which is not a lapsed
// invitation, so it has a figure of its own rather than sitting beside the expired ones.
//
// LockedOut counts the accounts whose lockout has not yet run out.
//
// SupplierLoginsOnPlaceholderAddresses is information, never a fault: the active supplier logins the ERP import
// created on an address that cannot receive mail, because the ERP held none for that supplier.
//
// OrganisationsByType has one row for every type of buying body, with active and inactive apart.
//
//
// WHAT NEEDS ATTENTION READS FROM HERE
//
// NeedsAttention gathers, under names of their own, the figures the needs attention section turns into items:
// accounts locked out, staff invitations that lapsed, and accounts that cannot sign in. It is computed from the
// figures above rather than counted again, so the item and the figure beside it on the screen cannot disagree, and
// it is left out of the answer the browser receives, because every number in it is already there.
//
// Lapsed means a staff invitation whose links all expired unused, with no sign-in ever. An invitation whose link
// was used, or whose email has not minted a link yet, is not lapsed. Staff and supplier figures stay apart here
// too: the locked-out and cannot-sign-in items link to the staff list, which lists staff only, so whether a
// supplier's figure becomes an item is the needs attention section's decision and not something a sum here could
// make for it.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using System.Text.Json.Serialization;
using MotsSupplierPortal.Domain.Organizations;

public sealed record DashboardPeopleAndAccessDto(
    DashboardAccountsDto Staff,
    DashboardAccountsDto Suppliers,
    IReadOnlyList<DashboardRoleCountDto> ActiveUsersByRole,
    DashboardStaffInvitedNeverSignedInDto StaffInvitedNeverSignedIn,
    IReadOnlyList<string> TwoFactorRequiredRoles,
    int SupplierLoginsOnPlaceholderAddresses,
    IReadOnlyList<DashboardOrganisationTypeCountDto> OrganisationsByType)
{
    [JsonIgnore]
    public DashboardPeopleAttentionFigures NeedsAttention => new(
        LockedOutStaff: Staff.LockedOut,
        LockedOutSuppliers: Suppliers.LockedOut,
        LapsedStaffInvitations: StaffInvitedNeverSignedIn.LinkExpired,
        StaffWhoCannotSignIn: Staff.CannotSignIn,
        SuppliersWhoCannotSignIn: Suppliers.CannotSignIn);
}

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

public sealed record DashboardStaffInvitedNeverSignedInDto(int LinkStillValid, int LinkExpired, int NoLinkYet, int LinkUsed);

public sealed record DashboardOrganisationTypeCountDto(OrganizationType Type, int Active, int Inactive);

public sealed record DashboardPeopleAttentionFigures(
    int LockedOutStaff,
    int LockedOutSuppliers,
    int LapsedStaffInvitations,
    int StaffWhoCannotSignIn,
    int SuppliersWhoCannotSignIn);
