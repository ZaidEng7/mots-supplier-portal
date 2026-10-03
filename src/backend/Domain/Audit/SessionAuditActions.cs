// The audit actions that record a session's life: signing in, a stolen refresh token being replayed, signing
// out, and revoking sessions.
//
// They are named once, here, because three places need the same list and each would drift from a copy of its
// own: the handlers that write the rows, the supplier's own activity trail that leaves them out, and the
// administrator's 24-hour activity count that leaves them out too.
//
//
// WHY THEY ARE LEFT OUT OF THOSE TWO READERS
//
// A supplier's trail is the record of what happened to their company's file. Every sign-in and sign-out by every
// member of their team would bury that record under rows nobody asked to see. The person's own security events
// stay in it: a password reset, a password change and a second-factor enrolment are changes to the account, and
// the account holder is the one person who must be able to see them.
//
// The administrator's count is a sign of activity on the registry. Sign-ins arrive with every working morning and
// would make the figure rise and fall with office hours instead of with the work.
//
// Staff reading the audit search still see every one of these rows. Nothing here hides them from the people whose
// job is to investigate a session.
//
//
// REFRESH_ROTATED IS NO LONGER WRITTEN
//
// A row for every token rotation recorded nothing a person did; it was the session ticking over each time the
// short-lived access token ran out. It stays in the list so that any rows written before it was dropped stay out of the two readers above.
// It has no constant of its own, so nothing can start writing it again by reaching for one.

namespace MotsSupplierPortal.Domain.Audit;

public static class SessionAuditActions
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string LoginLockedOut = "login_locked_out";
    public const string LoginBlockedMfaEnrollmentRequired = "login_blocked_mfa_enrollment_required";
    public const string LoginMfaFailed = "login_mfa_failed";
    public const string RefreshReuseDetected = "refresh_reuse_detected";
    public const string Logout = "logout";
    public const string SessionRevoked = "session_revoked";
    public const string SessionsRevokedAll = "sessions_revoked_all";

    public static readonly string[] All =
    [
        LoginSucceeded,
        LoginFailed,
        LoginLockedOut,
        LoginBlockedMfaEnrollmentRequired,
        LoginMfaFailed,
        RefreshReuseDetected,
        "refresh_rotated",
        Logout,
        SessionRevoked,
        SessionsRevokedAll,
    ];
}
