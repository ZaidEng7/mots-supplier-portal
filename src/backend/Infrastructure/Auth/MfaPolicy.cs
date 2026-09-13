// Which roles must carry a second factor at sign-in.
//
// Extracted when the security-posture screen needed to REPORT this list. It was resolved inline in the sign-in
// handler, and a screen reading the same configuration key with its own copy of the default would be a second
// source of truth for a security control: the kind that agrees on the day it is written and diverges the day
// somebody changes one of them. One expression, two readers.
//
// The rules mandate it for the system administrator at minimum. It is configurable so the list can widen without
// a code change.

namespace MotsSupplierPortal.Infrastructure.Auth;

using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Identity;

public static class MfaPolicy
{
    public static string[] RequiredRoles(IConfiguration configuration) =>
        configuration.GetSection("Mfa:RequiredRoles").Get<string[]>() ?? [Roles.SystemAdmin];
}
