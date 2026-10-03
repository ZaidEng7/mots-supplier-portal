// A supplier's own activity trail leaves out the session rows and keeps the account's security changes.
//
// Sign-ins, refused sign-ins and sign-outs are now stored, and they are recorded against the person, who belongs
// to the supplier, so without a rule every sign-in by every member of a team would land in the company's trail.
// The rule is SessionAuditActions, read by the trail's scope.
//
// The supplier here produces one of each by doing what a person does: signs in, gets a password wrong, changes the
// password, signs out, resets the password from a link and signs in again. The trail must show the change and the
// reset, which are changes to the account that its holder needs to see, and none of the session rows. The export
// is the same trail as a file and is held to the same rule.
//
// Two controls keep the exclusion from passing by accident. The session rows are present in storage for this
// person, so the trail is leaving them out rather than never having had them. And a member of staff searching
// the audit log by this person still finds them, so the rule belongs to the supplier's scope and has not removed
// the rows from the audit log as a whole.

namespace MotsSupplierPortal.Tests.Integration.Audit;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using MotsSupplierPortal.Tests.Integration.Auth;

[Collection(IntegrationTestCollection.Name)]
public sealed class OwnTrailSessionRowsTests(PostgresApiFixture fixture)
{
    private const string ChangedPassword = "ChangedOnScreen#2026!";
    private const string ResetPassword = "ResetFromLink#2026!";

    private static async Task<List<string>> ActionsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("action").GetString()!)];
    }

    [Fact]
    public async Task The_supplier_trail_shows_password_changes_and_resets_but_no_session_rows()
    {
        var (supplier, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Trail Session Rows Co");
        var userId = await SessionSteps.UserIdAsync(fixture, email);

        (await SessionSteps.SignInAsync(fixture, email, "NotThePassword#2026")).Response.StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await supplier.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = SupplierTestClient.Password, newPassword = ChangedPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (_, cookie) = await SessionSteps.SignInAsync(fixture, email, ChangedPassword);
        (await SessionSteps.LogoutAsync(fixture, cookie)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        string resetToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            resetToken = await scope.ServiceProvider.GetRequiredService<ISecurityTokenService>().IssueAsync(
                userId, SecurityTokenPurpose.PasswordReset, TimeSpan.FromHours(1), CancellationToken.None);
        }
        (await fixture.CreateRawClient().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = resetToken, newPassword = ResetPassword })).StatusCode.Should().Be(HttpStatusCode.OK);

        var (signIn, _) = await SessionSteps.SignInAsync(fixture, email, ResetPassword);
        var owner = fixture.CreateClient();
        owner.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SessionSteps.AccessTokenOf(signIn));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AuditLogs.Where(a => a.AggregateId == userId).Select(a => a.Action).ToListAsync();
            stored.Should().Contain([SessionAuditActions.LoginSucceeded, SessionAuditActions.LoginFailed, SessionAuditActions.Logout],
                "the control: the session rows exist, so leaving them out is the trail's doing");
        }

        var trail = await ActionsAsync(owner, "/api/v1/suppliers/me/audit?pageSize=100");
        trail.Should().Contain(["password_changed", "password_reset"],
            "a change to the account is the holder's to see");
        trail.Should().NotIntersectWith(SessionAuditActions.All);

        var export = await owner.GetStringAsync("/api/v1/suppliers/me/audit/export");
        export.Should().Contain("password_reset").And.Contain("password_changed");
        foreach (var action in SessionAuditActions.All)
        {
            export.Should().NotContain(action, "the export is the same trail as a file");
        }

        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        (await ActionsAsync(staff, $"/api/v1/audit?aggregateId={userId}&pageSize=100"))
            .Should().Contain([SessionAuditActions.LoginSucceeded, SessionAuditActions.Logout],
                "the control: staff searching the audit log still see a person's sessions");
    }
}
