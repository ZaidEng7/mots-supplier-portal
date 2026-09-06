using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Auth;

/// <summary>
/// Which roles must carry a second factor at sign-in.
///
/// <para>Extracted when SCR-726 needed to REPORT this list. It was resolved inline in LoginHandler, and
/// a screen that read the same configuration key with its own copy of the default would be a second
/// source of truth for a security control - the kind that agrees on the day it is written and diverges
/// the day someone changes one of them. One expression, two readers.</para>
///
/// <para>NFR-SEC-003 mandates MFA for system_admin at minimum. Configurable so the list can widen (e.g.
/// procurement_manager per FR-IAM-004) without a code change.</para>
/// </summary>
public static class MfaPolicy
{
    public static string[] RequiredRoles(IConfiguration configuration) =>
        configuration.GetSection("Mfa:RequiredRoles").Get<string[]>() ?? [Roles.SystemAdmin];
}
