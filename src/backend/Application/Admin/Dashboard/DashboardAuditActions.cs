// The audit actions the administrator's dashboard picks out by name: the security events it counts, the sensitive
// changes it lists, and the bookkeeping it leaves out of the recent activity feed.
//
// Every value here is an action some handler writes, spelled as the row stores it, and DashboardAuditActionLabelTests
// reads the code to hold that true. A misspelt name would not fail any query; it would count nothing and list
// nothing, and a zero on a security screen reads as good news.
//
//
// THE SECURITY EVENTS
//
// Five come from SessionAuditActions: a wrong password, a sign-in refused because the account is locked, a wrong
// second-factor code, a sign-in refused because the account's role needs a second factor it has not set up, and a
// refresh token presented again after it had been replaced, which is what a stolen one looks like. The other two are
// changes to an account's credentials made outside a session: a password reset through the emailed link, and an
// administrator removing a staff member's second factor.
//
// A successful sign-in, a sign-out and a revoked session are not counted. They are the normal life of a session, and
// a count of them would rise and fall with the working day.
//
//
// THE SENSITIVE CHANGES
//
// The changes to who can do what, and to what the portal sends outside itself: a role's permissions, a staff
// account invited, moved to another role, switched off or on, or stripped of its second factor; an API key issued
// or revoked; a system setting; the ERP connection and its push switch; and a supplier or a purchase order sent to
// the ERP again by hand.
//
// An ERP import is on the list only when somebody started it, because it creates and suspends suppliers in that
// person's name. The hourly sync starts one every hour as the system, and listing those would push every other
// change off a list of ten within a morning. A run from before the import recorded who started it was written as the
// system too, and cannot be told apart from a scheduled one, so it is left out with them.
//
//
// WHAT THE RECENT ACTIVITY FEED LEAVES OUT BY NAME
//
// The feed shows what people and other systems did, so every row the system wrote on its own is left out by its
// kind: the hourly sync's opening and closing rows among them. One action is also left out by name, whoever wrote
// it: a single failed attempt of the ERP push, which the push makes again by itself and which says nothing a person
// did. The push that gives up for good writes a row of its own, and that one stays.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Suppliers;

public static class DashboardAuditActions
{
    public const string SystemActorName = "system";

    public static readonly IReadOnlyList<string> SecurityEvents =
    [
        SessionAuditActions.LoginFailed,
        SessionAuditActions.LoginLockedOut,
        SessionAuditActions.LoginMfaFailed,
        SessionAuditActions.LoginBlockedMfaEnrollmentRequired,
        SessionAuditActions.RefreshReuseDetected,
        "password_reset",
        "staff_mfa_reset",
    ];

    public static readonly IReadOnlyList<string> SensitiveChanges =
    [
        "role_permissions_updated",
        "staff_invited",
        "staff_role_changed",
        "staff_deactivated",
        "staff_reactivated",
        "staff_mfa_reset",
        "api_key_created",
        "api_key_revoked",
        "setting.updated",
        "IntegrationConnectionUpdated",
        "IntegrationSupplierCreationChanged",
        "supplier.erp_push_retried",
        "award.erp_po_retried",
    ];

    public const string SensitiveWhenStartedByAPerson = SupplierAuditActions.ErpImportRun;

    public static readonly IReadOnlyList<string> LeftOutOfTheFeed =
    [
        SupplierAuditActions.ErpPushAttemptFailed,
    ];
}
