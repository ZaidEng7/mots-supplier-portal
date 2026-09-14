// What a staff account looks like: in the list, and in the administrator's fuller view.
//
// Neither shape carries supplier fields. An account created through a staff invitation never belongs to a
// supplier.
//
// The second-factor flag says whether an authenticator is currently enrolled. A system administrator needs
// one to hold a session at all, so an administrator locked out of their authenticator is a real support case,
// which is why resetting somebody else's exists.
//
//
// WHY THE FULLER VIEW EXISTS AT ALL
//
// Two screens were both top priority and neither had a screen or a route: the only staff routes were inviting
// somebody and accepting an invitation.
//
// So a system administrator could create an account and then never list, inspect, deactivate or reset one. An
// account created in error could not be removed, which is the half of that gap that is a security problem
// rather than an inconvenience.

namespace MotsSupplierPortal.Application.Auth;

using MotsSupplierPortal.Application.Common;

public sealed record StaffDto(Guid UserId, string Email, string FullName, string Role);

public sealed record StaffAccountDto(
    Guid UserId, string Email, string FullName, string? Role, bool IsActive,
    bool MfaEnabled,
    DateTimeOffset? LockoutEnd,
    int ActiveSessionCount);
