using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>The supplier-side sibling of StaffInviteTests's accept-flow test: the full real loop
/// for InviteSupplierUserHandler/AcceptSupplierUserInviteHandler - invite a supplier_user, mint a
/// real token exactly as the email job would, accept via the real HTTP endpoint, log in with the
/// new password, and confirm the JWT carries supplier_user's real (narrower than supplier_admin's)
/// permission set.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AcceptSupplierUserInviteTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Accepting_a_team_invite_lets_the_invited_user_log_in_with_supplier_user_s_permissions()
    {
        var admin = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Team Invite Accept Co");
        var email = $"teamaccept-{Guid.NewGuid():N}@example.com";

        var inviteResponse = await admin.PostAsJsonAsync("/api/v1/suppliers/me/users", new { email, fullName = "Invited Team Member" });
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var userId = (await inviteResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        string rawToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
            rawToken = await tokens.IssueAsync(userId, SecurityTokenPurpose.SupplierUserInvite, TimeSpan.FromDays(7), CancellationToken.None);
        }

        var anonymous = fixture.CreateClient();
        const string newPassword = "TeamAcceptPassword#2026!";
        var acceptResponse = await anonymous.PostAsJsonAsync("/api/v1/supplier-users/accept-invite", new { token = rawToken, password = newPassword });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password = newPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a real Identity user with a real password was created via UserManager, not a raw DB insert");

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;
        var claims = JwtClaims(accessToken);
        claims.Should().Contain(Permissions.SupplierEdit, "supplier_user's real permission set, proving the role assignment took effect");
        claims.Should().NotContain(Permissions.SupplierUserManage, "supplier_user must not silently gain supplier_admin's team-management permission");
    }

    private static IReadOnlyList<string> JwtClaims(string accessToken)
    {
        var payload = accessToken.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("perms", out var perms)) return [];
        return perms.ValueKind == JsonValueKind.Array
            ? [.. perms.EnumerateArray().Select(p => p.GetString()!)]
            : [perms.GetString()!];
    }
}
