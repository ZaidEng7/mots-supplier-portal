using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Auth;

public sealed record StaffDto(Guid UserId, string Email, string FullName, string Role);

// ─── T-077: administering a staff account, not merely creating one ────────────────────────────────
//
// SCR-701 and SCR-702 are both P0 in SCREEN-INVENTORY and neither had a screen OR an endpoint: the
// only staff routes were invite and accept-invite. `system_admin` could create an account and then
// never list, inspect, deactivate or reset MFA for one - so an account created in error could not be
// removed, which is the half of this that is a security gap rather than an inconvenience.

/// <summary>One staff account as an administrator sees it. No supplier fields: an account created
/// through the staff invite never carries a SupplierId (see InviteStaffHandler).</summary>
public sealed record StaffAccountDto(
    Guid UserId, string Email, string FullName, string? Role, bool IsActive,
    /// <summary>Whether MFA is currently enrolled. `system_admin` requires it to hold a session, so an
    /// administrator locked out of their authenticator is a real support case - see ResetStaffMfa.</summary>
    bool MfaEnabled,
    DateTimeOffset? LockoutEnd,
    int ActiveSessionCount);
