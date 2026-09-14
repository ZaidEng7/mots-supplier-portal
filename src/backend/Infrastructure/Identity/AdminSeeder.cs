// The two staff accounts a development host is seeded with: a system administrator and a reviewer.
//
//
// WHY A SEEDER AT ALL
//
// No path in the product creates a staff account. Registration only creates a supplier administrator tied to a
// company, so a real deployment seeds its first system administrator out of band, which is also what the
// integration tests do for themselves.
//
// This does the same thing at development startup, through the real identity framework rather than a raw
// insert, so the accounts are indistinguishable from ones a person created.
//
//
// ONE SHARED BODY, BECAUSE THE COPIES HAD ALREADY DRIFTED
//
// Both seeders were written out in full, and the reason for the password fallback was spelled out in one and
// summarised in the other, so the two had to be read together to learn one rule. The static analyser reported
// the file as wholly duplicated once a change touched both password lines at once. The duplication was real
// and older than that change; the change only made it countable.
//
// What stays with each seeder is what actually differs. The system administrator has to have its second factor
// pre-enrolled, because the rules require it for that role and there is no bootstrap enrolment flow for an
// account that cannot sign in yet. The reviewer is not in the list of roles that require it, so a
// password-only sign-in is correct for that role as shipped.
//
// The second factor's secret cannot be a constant: the identity framework only ever generates that key, so it
// is reset and read back, which is the same technique the test helper uses.
//
//
// THE PASSWORD, AND WHY A LITERAL IS DEFENSIBLE HERE
//
// It is read from configuration with a development-only fallback, which is the same shape the local
// container's own credentials take. A bare literal here would be indistinguishable to a scanner from a real
// production credential, and this way the file never has to be edited to carry one.
//
// The fallback is the shared demonstration password, so every seeded account on a development database has the
// same one. Three different fallbacks meant a walkthrough stopped twice to look up which account was the
// exception, and the exceptions were the two accounts a walk cannot get past without.
//
// Production is unaffected, because it supplies the configured key and never reaches the fallback, and the
// second factor is unchanged either way, which is what actually guards the administrator's account.
//
// The fallback is also kept simple to type live. The configured policy asks for length and no complexity, so
// adding punctuation and mixed case would be theatre.
//
//
// AN EXISTING ACCOUNT KEEPS ITS IDENTITY BUT NOT NECESSARILY ITS PASSWORD
//
// Consolidating on one password changed what the fallback IS, and an idempotent seeder returns early on a
// database created before that change, so the two accounts it was meant to fix kept the old value.
//
// Found by trying to sign in as the reviewer during a walkthrough on exactly such a database: the account
// existed, the published password was refused, and nothing anywhere said why.
//
// So the password is repaired rather than assumed. Development only, because the whole seeder refuses to run
// anywhere else, and it changes nothing on a database where the two already agree.
//
// Each seeder reports the password it actually used, and only on the run that created the account, so the
// caller has something to print once and nothing to print on later restarts.

namespace MotsSupplierPortal.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Identity;

internal static class DevStaffSeed
{
    internal static async Task<(AppUser User, string Password)?> CreateAsync(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        string email,
        string fullName,
        string role,
        string passwordKey)
    {
        if (await userManager.FindByEmailAsync(email) is { } existing)
        {
            var configured = configuration[passwordKey] ?? DevDataSeeder.Password;
            if (!await userManager.CheckPasswordAsync(existing, configured))
            {
                var reset = await userManager.RemovePasswordAsync(existing);
                if (reset.Succeeded) await userManager.AddPasswordAsync(existing, configured);
            }
            return null;
        }

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

    public static string? PasswordUsed { get; private set; }

    public static string? TotpSecret { get; private set; }

    public static async Task SeedAsync(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        var seeded = await DevStaffSeed.CreateAsync(
            userManager, configuration, Email, "Demo System Admin", Roles.SystemAdmin, "DevSeed:AdminPassword");
        if (seeded is not { } account) return;

        await userManager.ResetAuthenticatorKeyAsync(account.User);
        TotpSecret = await userManager.GetAuthenticatorKeyAsync(account.User);
        await userManager.SetTwoFactorEnabledAsync(account.User, true);
        PasswordUsed = account.Password;
    }
}

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
