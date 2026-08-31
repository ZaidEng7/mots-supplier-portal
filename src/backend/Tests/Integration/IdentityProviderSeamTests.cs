using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #7/Stage D: proves IIdentityProvider is a real seam - LoginHandler depends on the
/// interface, not on ASP.NET Core Identity's UserManager/SignInManager directly - by swapping in
/// a fake implementation and observing the real, unfaked /api/v1/auth/login endpoint's behavior
/// actually change as a result. An interface nothing exercises differently is not proof of
/// anything; this drives the SAME real HTTP endpoint through the SAME real host, with only the
/// identity-verification step swapped out.
///
/// Uses WithWebHostBuilder off the shared fixture (same already-running Postgres/MinIO/ClamAV
/// containers, ConfigureWebHost re-applies the same connection settings) rather than a second
/// full fixture - overriding IIdentityProvider on the SHARED fixture itself would break every
/// other integration test that logs in through the real Identity store.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IdentityProviderSeamTests(PostgresApiFixture fixture)
{
    /// <summary>Always succeeds sign-in regardless of the password given - the one behavior a
    /// real ASP.NET Core Identity check could never produce for a wrong password, so success here
    /// can only be explained by LoginHandler actually calling this fake, not the real Identity
    /// store underneath it.</summary>
    private sealed class AlwaysSucceedsIdentityProvider(AppUser user) : IIdentityProvider
    {
        public Task<AppUser?> FindByEmailAsync(string email) => Task.FromResult<AppUser?>(user);
        public Task<IdentitySignInResult> CheckPasswordSignInAsync(AppUser u, string password, bool lockoutOnFailure) => Task.FromResult(IdentitySignInResult.Success);
        public Task<IReadOnlyList<string>> GetRolesAsync(AppUser u) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> VerifyTwoFactorTokenAsync(AppUser u, string code) => Task.FromResult(false);
        public Task<bool> RedeemTwoFactorRecoveryCodeAsync(AppUser u, string code) => Task.FromResult(false);
    }

    private async Task<AppUser> GetRealUserAsync(string email)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.SingleAsync(u => u.Email == email);
    }

    [Fact]
    public async Task Swapping_in_a_fake_IIdentityProvider_makes_a_wrong_password_succeed()
    {
        var (_, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Seam Test Co");
        var realUser = await GetRealUserAsync(email);

        await using var fakeFactory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IIdentityProvider>(_ => new AlwaysSucceedsIdentityProvider(realUser))));
        var fakeClient = fakeFactory.CreateClient();

        var response = await fakeClient.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "definitely-the-wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the swapped-in fake ignores the password entirely - this can only pass if LoginHandler actually goes through IIdentityProvider");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_same_wrong_password_is_rejected_by_the_real_unfaked_provider()
    {
        // Control: the exact same wrong-password login against the ordinary fixture (real
        // AspNetIdentityProvider, no swap) - proves the success above is caused by the swap, not
        // by some unrelated bug that would let any wrong password through regardless.
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Seam Control Co");
        _ = client;

        var response = await fixture.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "definitely-the-wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
