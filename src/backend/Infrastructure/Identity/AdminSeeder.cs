using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Infrastructure.Identity;

/// <summary>
/// Development-only seed of one system_admin account, for local demo/manual testing.
///
/// There is no in-product path to create a staff/admin user (registration only creates a
/// supplier_admin tied to a Supplier - see RegisterSupplierHandler); a real deployment seeds its
/// first system_admin out of band, same as StaffTestClient does for integration tests. This does
/// the same thing at Development startup: creates the user (if absent) through UserManager -
/// real ASP.NET Core Identity, not a raw DB insert - and pre-enrolls TOTP with a fixed secret
/// since NFR-SEC-003 mandates MFA for system_admin and there is no bootstrap enrollment flow for
/// an account that cannot yet log in.
///
/// The password is read from configuration with a dev-only fallback, same shape as
/// docker-compose.yml's own dev credentials (NFR-SEC-007/S2068: "Credentials are read from the
/// environment with a dev-only default, so this file never has to be edited to carry a real one")
/// - a bare string literal here is indistinguishable, to a static scanner, from a real hardcoded
/// production credential; this file never has to be edited to carry a real one either.
/// </summary>
public static class AdminSeeder
{
    public const string Email = "admin@mots.local";

    /// <summary>Actual password used on the run that created the account, filled in by
    /// <see cref="SeedAsync"/> so the caller can print/log it once - same pattern as
    /// <see cref="TotpSecret"/>.</summary>
    public static string? PasswordUsed { get; private set; }

    /// <summary>Base32 TOTP secret actually stored for the seeded account, filled in by
    /// <see cref="SeedAsync"/> so the caller can print/log it once, on the run that created the
    /// user. ASP.NET Core Identity only ever generates this key (<c>ResetAuthenticatorKeyAsync</c>)
    /// - there is no "set a chosen secret" API - so it cannot be a compile-time constant.</summary>
    public static string? TotpSecret { get; private set; }

    public static async Task SeedAsync(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        var existing = await userManager.FindByEmailAsync(Email);
        if (existing is not null) return;

        // Identity policy here is length>=12, no complexity requirement (Program.cs) - the
        // fallback is kept simple to type live rather than adding punctuation/case-mixing
        // nothing actually enforces.
        var password = configuration["DevSeed:AdminPassword"] ?? "motsadmin2026";

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = Email,
            Email = Email,
            FullName = "Demo System Admin",
            EmailConfirmed = true,
            IsActive = true,
            SupplierId = null,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not seed the demo system_admin user: " +
                string.Join(", ", created.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, Roles.SystemAdmin);

        // Same technique as StaffTestClient.CreateWithMfaAsync: Identity only generates this key,
        // it cannot be assigned a chosen value, so it must be read back after resetting it.
        await userManager.ResetAuthenticatorKeyAsync(user);
        TotpSecret = await userManager.GetAuthenticatorKeyAsync(user);
        await userManager.SetTwoFactorEnabledAsync(user, true);
        PasswordUsed = password;
    }
}

/// <summary>
/// Development-only seed of one onboarding_reviewer account (the Ministry back-office reviewer
/// persona), for local demo/manual testing. Same rationale as <see cref="AdminSeeder"/>: no
/// in-product path creates staff users, so this goes through UserManager directly, and the
/// password is read from configuration with a dev-only fallback for the same reason.
///
/// Unlike system_admin, onboarding_reviewer is NOT in LoginHandler's default
/// <c>Mfa:RequiredRoles</c> list (only system_admin is, and this repo's appsettings do not
/// override that) - so no TOTP enrollment is needed here, password-only login is correct for
/// this role as shipped.
/// </summary>
public static class ReviewerSeeder
{
    public const string Email = "reviewer@mots.local";

    public static string? PasswordUsed { get; private set; }

    public static async Task SeedAsync(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        var existing = await userManager.FindByEmailAsync(Email);
        if (existing is not null) return;

        var password = configuration["DevSeed:ReviewerPassword"] ?? "motsreview2026";

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = Email,
            Email = Email,
            FullName = "Demo Ministry Reviewer",
            EmailConfirmed = true,
            IsActive = true,
            SupplierId = null,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not seed the demo onboarding_reviewer user: " +
                string.Join(", ", created.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, Roles.OnboardingReviewer);
        PasswordUsed = password;
    }
}
