// The session audit actions are stored values, and the list of them decides what two readers leave out.
//
// Each constant is the string written into the audit table and searched for afterwards, so it is pinned to its
// literal here rather than compared with itself. Renaming one would leave every row already written under the old
// name, and the audit search would stop finding them without an error anywhere.
//
// The list is what a supplier's own trail and the administrator's 24-hour count leave out. It is pinned as written,
// so dropping an action from it puts every row of that action back into both readers, and adding one hides a row
// someone may need to see; either is a decision, and this makes it a visible one. refresh_rotated is in it on
// purpose with no constant of its own: nothing writes it any more, and rows written before that stay hidden.

namespace MotsSupplierPortal.Tests.Unit.Audit;

using FluentAssertions;
using MotsSupplierPortal.Domain.Audit;

public sealed class SessionAuditActionsTests
{
    [Fact]
    public void Each_action_is_the_string_stored_in_the_audit_table()
    {
        SessionAuditActions.LoginSucceeded.Should().Be("login_succeeded");
        SessionAuditActions.LoginFailed.Should().Be("login_failed");
        SessionAuditActions.LoginLockedOut.Should().Be("login_locked_out");
        SessionAuditActions.LoginBlockedMfaEnrollmentRequired.Should().Be("login_blocked_mfa_enrollment_required");
        SessionAuditActions.LoginMfaFailed.Should().Be("login_mfa_failed");
        SessionAuditActions.RefreshReuseDetected.Should().Be("refresh_reuse_detected");
        SessionAuditActions.Logout.Should().Be("logout");
        SessionAuditActions.SessionRevoked.Should().Be("session_revoked");
        SessionAuditActions.SessionsRevokedAll.Should().Be("sessions_revoked_all");
    }

    [Fact]
    public void The_list_the_readers_leave_out_is_exactly_these_actions()
    {
        SessionAuditActions.All.Should().BeEquivalentTo(
        [
            "login_succeeded",
            "login_failed",
            "login_locked_out",
            "login_blocked_mfa_enrollment_required",
            "login_mfa_failed",
            "refresh_reuse_detected",
            "refresh_rotated",
            "logout",
            "session_revoked",
            "sessions_revoked_all",
        ]);
    }
}
