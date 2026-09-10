using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A seeded staff account whose password has drifted from the published one is repaired, not skipped.
///
/// <para><b>The defect this closes.</b> The seeders are idempotent - they return early when the account
/// already exists, which is what makes restarting the API safe. But "one password across every seeded
/// account" changed what the fallback IS, and an idempotent seeder returning early cannot apply a
/// change to an account it created before it. So on any database older than that change, the two
/// accounts the change was written for kept the old value.</para>
///
/// <para>Found by hand, during a walkthrough: <c>reviewer@mots.local</c> existed, the password
/// published in RUNBOOK.md was refused, and nothing anywhere said why. This is the regression test for
/// the repair, and it exercises it through the real public entry point rather than the internal helper
/// underneath, because the entry point is what Program.cs actually calls.</para>
///
/// <para>The account is removed afterwards whatever happens, so the shared fixture database is left as
/// it was found - no other test in this suite touches this address, and none asserts a user total.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SeededPasswordRepairTests(PostgresApiFixture fixture)
{
    private const string DriftedPassword = "an-old-password-from-before";

    private static IConfiguration NoOverrides => new ConfigurationBuilder().Build();

    [Fact]
    public async Task An_existing_seeded_account_whose_password_drifted_is_repaired()
    {
        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        await RemoveReviewerAsync(userManager);
        try
        {
            // The database as it is on any machine created before the one-password change: the account
            // exists, and its password is not the published one.
            var drifted = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = ReviewerSeeder.Email,
                Email = ReviewerSeeder.Email,
                FullName = "Drifted Reviewer",
                EmailConfirmed = true,
                IsActive = true,
                SupplierId = null,
            };
            (await userManager.CreateAsync(drifted, DriftedPassword)).Succeeded.Should().BeTrue();

            // The precondition, asserted rather than assumed. Without this the test would pass even if
            // the seeder did nothing and the account had always held the published password.
            (await userManager.CheckPasswordAsync(drifted, DevDataSeeder.Password)).Should().BeFalse();

            await ReviewerSeeder.SeedAsync(userManager, NoOverrides);

            var repaired = await userManager.FindByEmailAsync(ReviewerSeeder.Email);
            repaired.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(repaired!, DevDataSeeder.Password))
                .Should().BeTrue("the published password is the one RUNBOOK.md tells a person to type");
            (await userManager.CheckPasswordAsync(repaired!, DriftedPassword))
                .Should().BeFalse("the drifted password must not keep working alongside the repaired one");

            // Idempotent, and still idempotent about identity: the repair must not create a second
            // account, and it must leave the one it found alone on a second run.
            await ReviewerSeeder.SeedAsync(userManager, NoOverrides);
            var again = await userManager.FindByEmailAsync(ReviewerSeeder.Email);
            again!.Id.Should().Be(repaired!.Id);
            (await userManager.CheckPasswordAsync(again, DevDataSeeder.Password)).Should().BeTrue();
        }
        finally
        {
            await RemoveReviewerAsync(userManager);
        }
    }

    [Fact]
    public async Task A_configured_password_wins_over_the_shared_fallback()
    {
        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        // Production supplies this key and never reaches the fallback, so the repair has to honour it
        // too - repairing a deployed account to a password published in a runbook would be the exact
        // opposite of the point.
        const string configured = "a-configured-password-not-the-fallback";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DevSeed:ReviewerPassword"] = configured })
            .Build();

        await RemoveReviewerAsync(userManager);
        try
        {
            await ReviewerSeeder.SeedAsync(userManager, configuration);

            var created = await userManager.FindByEmailAsync(ReviewerSeeder.Email);
            created.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(created!, configured)).Should().BeTrue();
            (await userManager.CheckPasswordAsync(created!, DevDataSeeder.Password))
                .Should().BeFalse("the configured value is the one that was asked for");
        }
        finally
        {
            await RemoveReviewerAsync(userManager);
        }
    }

    private static async Task RemoveReviewerAsync(UserManager<AppUser> userManager)
    {
        if (await userManager.FindByEmailAsync(ReviewerSeeder.Email) is { } existing)
        {
            await userManager.DeleteAsync(existing);
        }
    }
}
