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
/// <summary>
/// The half of a development-only staff seed that is the same whoever is being seeded: look for the
/// account, read its password with the shared fallback, create it through real Identity, put it in
/// its role.
///
/// <para>Both seeders below were written with this body spelled out, and the copies had already begun
/// to drift: the reason for the password fallback was written out in full in one and summarised in the
/// other, so the two now had to be read together to learn one rule. Sonar reported the file as 100%
/// duplicated on new code once a change touched both password lines at once. The duplication was real
/// and older than that change; the change only made it countable.</para>
///
/// <para>What stays with each seeder is what actually differs: system_admin has to pre-enroll TOTP,
/// because NFR-SEC-003 mandates MFA for that role and there is no bootstrap enrollment flow for an
/// account that cannot log in yet. onboarding_reviewer is not in <c>Mfa:RequiredRoles</c>, so
/// password-only login is correct for it as shipped.</para>
/// </summary>
internal static class DevStaffSeed
{
    /// <summary>
    /// Returns the created user and the password it was given, or null if the account already exists -
    /// which is how a caller knows whether it has anything to print, and keeps the seeder idempotent
    /// across restarts of a development host.
    /// </summary>
    internal static async Task<(AppUser User, string Password)?> CreateAsync(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        string email,
        string fullName,
        string role,
        string passwordKey)
    {
        if (await userManager.FindByEmailAsync(email) is not null) return null;

        // Identity policy here is length>=12, no complexity requirement (Program.cs) - the fallback is
        // kept simple to type live rather than adding punctuation/case-mixing nothing actually enforces.
        //
        // The fallback is DevDataSeeder's own constant, so every seeded account on a development
        // database shares one password. Three different fallbacks meant a walkthrough stopped twice to
        // look up which account was the exception, and the exceptions were the two accounts - the
        // onboarding reviewer and the bootstrap admin - that a walk cannot get past without.
        //
        // Production is unaffected: it supplies the configured key and never reaches the fallback. MFA
        // is unchanged either way - system_admin still requires a TOTP code, which is what actually
        // guards that account.
        var password = configuration[passwordKey] ?? DevDataSeeder.Password;

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true,
            SupplierId = null,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed the demo {role} user: " +
                string.Join(", ", created.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, role);
        return (user, password);
    }
}

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
        var seeded = await DevStaffSeed.CreateAsync(
            userManager, configuration, Email, "Demo System Admin", Roles.SystemAdmin, "DevSeed:AdminPassword");
        if (seeded is not { } account) return;

        // Same technique as StaffTestClient.CreateWithMfaAsync: Identity only generates this key, it
        // cannot be assigned a chosen value, so it must be read back after resetting it.
        await userManager.ResetAuthenticatorKeyAsync(account.User);
        TotpSecret = await userManager.GetAuthenticatorKeyAsync(account.User);
        await userManager.SetTwoFactorEnabledAsync(account.User, true);
        PasswordUsed = account.Password;
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
        var seeded = await DevStaffSeed.CreateAsync(
            userManager, configuration, Email, "Demo Ministry Reviewer", Roles.OnboardingReviewer, "DevSeed:ReviewerPassword");
        PasswordUsed = seeded?.Password;
    }
}
