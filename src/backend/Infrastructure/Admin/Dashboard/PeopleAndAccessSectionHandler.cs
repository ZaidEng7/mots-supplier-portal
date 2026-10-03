// The people and access section of the administrator's dashboard: who has an account, what they may do, who is
// signed in, who was invited and has not arrived, and who cannot get in.
//
// It is resolved from a scope of its own and runs beside the other sections, as GetAdminDashboardHandler
// describes. Every moment it compares against is the request's AsOf, so a lockout or a link that runs out while
// the dashboard is being built is judged against the same instant as everything else on it. Sessions are the
// exception, for the reason given below.
//
// It reads accounts, roles, sessions, single-use tokens, one kind of audit row and the buying bodies, and none of
// the tables the row-scope guard watches, so it needs no exemption there. It names no permission either: who may
// see it is decided in GetAdminDashboardHandler.
//
//
// STAFF ARE THE ACCOUNTS WITHOUT A COMPANY
//
// The same test the staff list and the staff invitation draw, so the two counts here and the staff list's rows are
// the same accounts. Every per-account figure is computed for staff and suppliers apart and handed back apart.
// Nothing here adds them together. The figures other than the active and inactive counts are over active accounts
// only: a deactivated account cannot sign in, so its lockout, its missing second factor or its open invitation
// asks nothing of anybody.
//
// A role's row counts the active accounts holding it, and the roles come from the role table, so a role nobody
// holds still appears with nought rather than vanishing. The per-person figure beside them counts an account
// holding two roles once.
//
//
// SESSIONS ARE COUNTED BY ActiveSessionCounts
//
// By sign-in rather than by token row, and alive while RefreshToken.Active says so. That rule reads the database's
// clock, not AsOf, and it is used unchanged so the total here and the figure beside each account on the staff list
// cannot disagree about which sessions are open.
//
//
// AN INVITATION IS PENDING WHILE ITS LINK WORKS AND NO LINK HAS BEEN USED
//
// A link works while its token has not been used and has not expired. Staff invitations are the staff invitation
// tokens and a supplier's team invitations are the supplier invitation tokens. The tokens' purpose decides which
// side an invitation is on.
//
// One person can hold several working links. The invitation email is a background job that mints a new token on
// every attempt, and a failed send is retried, so a person whose first sends failed holds one token per attempt.
// So invitations are counted by person, never by token. And once any one of a person's links has been used, the
// invitation was accepted and the earlier links that never arrived do not make it pending again.
//
//
// INVITED AND NEVER SIGNED IN, STAFF ONLY
//
// An active staff account with a staff_invited row in the audit log, the row InviteStaffHandler writes against the
// new account, and no session row at all, live or ended. Session rows are revoked, never deleted, so no row ever
// means no sign-in ever. That figure depends on it staying true: a cleanup that deleted old session rows would
// make everybody it touched look as if they had never signed in.
//
// They are split four ways by what became of their staff invitation links. Still valid is pending in the sense
// above. Used means a link was consumed, so the person reached the page that sets a password and has not signed in
// since. Expired means links were minted and every one ran out unused: the lapsed invitation. No link yet means
// none was ever minted, which is the moment between the invitation and its email job's first attempt, or an email
// job that never got that far; a failing email shows in system health, not here.
//
// Supplier invitations cannot be followed this way. InviteSupplierUserHandler records them against the supplier
// rather than the person, so a supplier invitation is visible only while its token lives, which is what the
// supplier side's pending figure already counts. The supplier logins the ERP import makes are not invited at all.
//
//
// CANNOT SIGN IN
//
// An active account holding a role that MfaPolicy says requires a second factor, without one set up. The roles are
// read through MfaPolicy, the list sign-in itself enforces, and matched without regard to case as sign-in matches
// them, so this figure is exactly the set of accounts sign-in would refuse for that reason. Enrolling needs a
// session, which is the thing they cannot get, so resetting their second factor would not help them, and the
// figure carries no such suggestion.
//
//
// LOCKED OUT AND THE PLACEHOLDER ADDRESSES
//
// Locked out means the lockout end is later than AsOf. A lockout that has run out is history, not a lock.
//
// The placeholder figure counts active supplier logins on the address the ERP import invents when the ERP holds
// none, ending in @erp-import.invalid in any letter case. Such an address can never receive mail, so the person
// cannot reset a password or be told anything. It is information about the registry rather than a fault.
//
//
// ORGANISATIONS
//
// Every type of buying body appears, with active and inactive apart, including a type with none.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Organizations;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class PeopleAndAccessSectionHandler(AppDbContext db, IConfiguration configuration)
    : IDashboardSectionHandler<DashboardPeopleAndAccessDto>
{
    private const string StaffInvitedAction = "staff_invited";

    private const string StaffInvitedAggregate = "AppUser";

    public async Task<DashboardPeopleAndAccessDto> RunAsync(DashboardRequest request, CancellationToken ct)
    {
        var asOf = request.AsOf;
        var placeholderSuffix = "@" + ErpImportAdmission.PlaceholderDomain;

        var roles = await db.Roles
            .Where(r => r.Name != null)
            .Select(r => new
            {
                r.Id,
                Name = r.Name!,
                ActiveUsers = db.UserRoles.Count(ur => ur.RoleId == r.Id && db.Users.Any(u => u.Id == ur.UserId && u.IsActive)),
            })
            .ToListAsync(ct);

        var twoFactorRequiredRoles = MfaPolicy.RequiredRoles(configuration);
        Guid[] twoFactorRoleIds =
        [
            .. roles
                .Where(r => twoFactorRequiredRoles.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.Id),
        ];

        var accounts = await db.Users
            .Select(u => new
            {
                Supplier = u.SupplierId != null,
                u.IsActive,
                HasRole = db.UserRoles.Any(ur => ur.UserId == u.Id),
                LockedOut = u.LockoutEnd != null && u.LockoutEnd > asOf,
                CannotSignIn = !u.TwoFactorEnabled
                    && db.UserRoles.Any(ur => ur.UserId == u.Id && twoFactorRoleIds.Contains(ur.RoleId)),
                Placeholder = u.Email != null && u.Email.ToLower().EndsWith(placeholderSuffix),
            })
            .GroupBy(x => new { x.Supplier, x.IsActive })
            .Select(g => new
            {
                g.Key.Supplier,
                g.Key.IsActive,
                Accounts = g.Count(),
                WithARole = g.Count(x => x.HasRole),
                LockedOut = g.Count(x => x.LockedOut),
                CannotSignIn = g.Count(x => x.CannotSignIn),
                Placeholder = g.Count(x => x.Placeholder),
            })
            .ToListAsync(ct);

        var (staffSessions, supplierSessions) = await ActiveSessionCounts.ByAccountKindAsync(db, ct);

        var pendingStaff = await db.Users.Where(u => u.IsActive)
            .CountAsync(InvitationPending(SecurityTokenPurpose.StaffInvite, asOf), ct);
        var pendingSuppliers = await db.Users.Where(u => u.IsActive)
            .CountAsync(InvitationPending(SecurityTokenPurpose.SupplierUserInvite, asOf), ct);

        var invitedNeverSignedIn = await db.Users
            .Where(u => u.SupplierId == null && u.IsActive)
            .Where(u => db.AuditLogs.Any(a =>
                a.Action == StaffInvitedAction && a.AggregateType == StaffInvitedAggregate && a.AggregateId == u.Id))
            .Where(u => !db.RefreshTokens.Any(t => t.UserId == u.Id))
            .Select(u => new
            {
                Used = db.SecurityTokens.Any(t =>
                    t.UserId == u.Id && t.Purpose == SecurityTokenPurpose.StaffInvite && t.ConsumedAt != null),
                Valid = db.SecurityTokens.Any(t =>
                    t.UserId == u.Id && t.Purpose == SecurityTokenPurpose.StaffInvite && t.ConsumedAt == null
                    && t.ExpiresAt > asOf),
                Minted = db.SecurityTokens.Any(t => t.UserId == u.Id && t.Purpose == SecurityTokenPurpose.StaffInvite),
            })
            .ToListAsync(ct);

        var organisations = await db.Organizations
            .GroupBy(o => new { o.OrganizationType, o.IsActive })
            .Select(g => new { g.Key.OrganizationType, g.Key.IsActive, Count = g.Count() })
            .ToListAsync(ct);

        DashboardAccountsDto Accounts(bool supplier, ActiveSessionTotals sessions, int pendingInvitations)
        {
            var active = accounts.SingleOrDefault(a => a.Supplier == supplier && a.IsActive);
            var inactive = accounts.SingleOrDefault(a => a.Supplier == supplier && !a.IsActive);
            return new DashboardAccountsDto(
                Active: active?.Accounts ?? 0,
                Inactive: inactive?.Accounts ?? 0,
                ActiveWithARole: active?.WithARole ?? 0,
                ActiveSessions: sessions.Sessions,
                PeopleWithActiveSessions: sessions.People,
                PendingInvitations: pendingInvitations,
                LockedOut: active?.LockedOut ?? 0,
                CannotSignIn: active?.CannotSignIn ?? 0);
        }

        int Organisations(OrganizationType type, bool isActive) =>
            organisations.Where(o => o.OrganizationType == type && o.IsActive == isActive).Sum(o => o.Count);

        return new DashboardPeopleAndAccessDto(
            Staff: Accounts(supplier: false, staffSessions, pendingStaff),
            Suppliers: Accounts(supplier: true, supplierSessions, pendingSuppliers),
            ActiveUsersByRole:
            [
                .. roles
                    .OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => new DashboardRoleCountDto(r.Name, r.ActiveUsers)),
            ],
            StaffInvitedNeverSignedIn: new DashboardStaffInvitedNeverSignedInDto(
                LinkStillValid: invitedNeverSignedIn.Count(i => !i.Used && i.Valid),
                LinkExpired: invitedNeverSignedIn.Count(i => !i.Used && !i.Valid && i.Minted),
                NoLinkYet: invitedNeverSignedIn.Count(i => !i.Minted),
                LinkUsed: invitedNeverSignedIn.Count(i => i.Used)),
            TwoFactorRequiredRoles: twoFactorRequiredRoles,
            SupplierLoginsOnPlaceholderAddresses:
                accounts.SingleOrDefault(a => a.Supplier && a.IsActive)?.Placeholder ?? 0,
            OrganisationsByType:
            [
                .. Enum.GetValues<OrganizationType>().Select(type => new DashboardOrganisationTypeCountDto(
                    type, Organisations(type, isActive: true), Organisations(type, isActive: false))),
            ]);
    }

    private Expression<Func<AppUser, bool>> InvitationPending(SecurityTokenPurpose purpose, DateTimeOffset asOf) =>
        u => db.SecurityTokens.Any(t =>
                 t.UserId == u.Id && t.Purpose == purpose && t.ConsumedAt == null && t.ExpiresAt > asOf)
             && !db.SecurityTokens.Any(t => t.UserId == u.Id && t.Purpose == purpose && t.ConsumedAt != null);
}
