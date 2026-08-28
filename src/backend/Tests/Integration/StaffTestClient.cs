using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A logged-in staff (back-office) identity, for endpoints guarded by a staff permission.
///
/// This did not exist before MSP-63 - FlaggedFieldEnforcementTests records the gap explicitly and
/// works around it by driving the domain directly. That workaround is reasonable for domain rules,
/// but it cannot exercise an endpoint's permission guard, its HTTP contract, or its status codes.
/// For a ticket that was once reported as built on the strength of an enum, testing through the
/// real endpoint with a real permission is the difference between evidence and inference.
///
/// Staff users have SupplierId = null, which is what makes them "staff" to IScopeContext: the row
/// scoping treats a null SupplierId as unrestricted (see GetAuditLogHandler for the same rule).
/// </summary>
public static class StaffTestClient
{
    public const string Password = "StaffIntegration#2026!";

    public static async Task<HttpClient> CreateAsync(PostgresApiFixture fixture, string role)
    {
        var client = fixture.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ministry.example";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                FullName = "Integration Staff",
                EmailConfirmed = true,
                IsActive = true,
                SupplierId = null,
            };

            var created = await userManager.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the staff user: " +
                    string.Join(", ", created.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(user, role);
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }
}
