using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-716. Interface strings were compiled into the SPA bundle, so correcting one word meant a release.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class UiStringOverrideTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    [Fact]
    public async Task The_bundle_read_is_anonymous_because_the_login_screen_needs_it()
    {
        // Not a convenience. The login screen, the registration form and SCR-047's interstitial all render
        // before anyone is authenticated, and a reworded label that only appeared after sign-in would be a
        // worse inconsistency than no rewording at all.
        var anonymous = fixture.CreateRawClient();

        var response = await anonymous.GetAsync("/api/v1/ui-strings/ar");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // An empty bundle is the normal answer on a fresh database: no overrides means the shipped strings
        // stand. It must not be an error, or every clean deployment would log one on first paint.
        var bundle = await response.Content.ReadFromJsonAsync<JsonElement>();
        bundle.GetProperty("language").GetString().Should().Be("ar");
        bundle.TryGetProperty("strings", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_language_the_product_does_not_ship_is_a_404_not_an_empty_bundle()
    {
        var anonymous = fixture.CreateRawClient();

        // The distinction matters: an empty bundle means "no rewordings", and answering that for French
        // would tell a caller French exists and simply has none.
        (await anonymous.GetAsync("/api/v1/ui-strings/fr")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Both controls.
        (await anonymous.GetAsync("/api/v1/ui-strings/ar")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/ui-strings/en")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_override_is_saved_read_back_and_removable()
    {
        var admin = await AdminAsync();
        var anonymous = fixture.CreateRawClient();
        var key = $"test.scr716.{Guid.NewGuid():N}"[..30];

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/ui-strings/ar/{key}", new { value = "نص معدّل" });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // Read back through the PUBLIC endpoint, not the admin list: that is the one the SPA uses, and an
        // override the admin screen can see but the app cannot fetch would be invisible to every user.
        var bundle = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/ui-strings/ar");
        bundle.GetProperty("strings").GetProperty(key).GetString().Should().Be("نص معدّل");

        // A second PUT is an update, not a duplicate: the identity of the thing is (key, language), and an
        // administrator editing a label does not know or care whether a row exists.
        (await admin.PutAsJsonAsync($"/api/v1/admin/ui-strings/ar/{key}", new { value = "صياغة أخرى" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/ui-strings/ar");
        updated.GetProperty("strings").GetProperty(key).GetString().Should().Be("صياغة أخرى");

        // The same key in the other language is a DIFFERENT override: rewording an Arabic label is not
        // rewording the English one.
        var english = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/ui-strings/en");
        english.GetProperty("strings").TryGetProperty(key, out _).Should().BeFalse();

        (await admin.DeleteAsync($"/api/v1/admin/ui-strings/ar/{key}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Removing the override is the only way back to the shipped string, so the key must be gone
        // rather than present and empty.
        var restored = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/ui-strings/ar");
        restored.GetProperty("strings").TryGetProperty(key, out _).Should().BeFalse();

        // And a second delete says so, rather than reporting a restoration that did not happen.
        (await admin.DeleteAsync($"/api/v1/admin/ui-strings/ar/{key}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_dotted_key_survives_the_route_whole()
    {
        // i18n keys contain dots, and the route takes the key as a catch-all segment for exactly this
        // reason. A key truncated at the first dot would silently override the wrong thing - or nothing.
        var admin = await AdminAsync();
        var anonymous = fixture.CreateRawClient();
        const string key = "proposal.errors.reviseFailed";

        // T-073: a real product string, overridden globally. The delete was the last line, so a
        // failing assertion left every later test - and every screen in the suite's app - reading
        // this test's wording. It is a finally now.
        await using (new RevertUiString(admin, "en", key))
        {
            await admin.PutAsJsonAsync($"/api/v1/admin/ui-strings/en/{key}", new { value = "Could not record it" });

            var bundle = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/ui-strings/en");
            bundle.GetProperty("strings").GetProperty(key).GetString().Should().Be("Could not record it");
        }
    }

    [Fact]
    public async Task An_empty_override_is_refused()
    {
        // Blanking a label would be indistinguishable from a missing translation, and a user could not
        // report what they cannot see. Removing the override is how you go back.
        var admin = await AdminAsync();

        (await admin.PutAsJsonAsync("/api/v1/admin/ui-strings/ar/test.blank", new { value = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Control.
        (await admin.PutAsJsonAsync("/api/v1/admin/ui-strings/ar/test.blank", new { value = "شيء" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await admin.DeleteAsync("/api/v1/admin/ui-strings/ar/test.blank");
    }

    [Fact]
    public async Task Only_an_administrator_writes_them()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.OnboardingReviewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.PutAsJsonAsync("/api/v1/admin/ui-strings/ar/test.forbidden", new { value = "لا" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} must not be able to reword the product");
            (await staff.GetAsync("/api/v1/admin/ui-strings/")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "String Outsider Co");
        (await supplier.PutAsJsonAsync("/api/v1/admin/ui-strings/ar/test.forbidden", new { value = "لا" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The control, and the asymmetry worth stating: the same supplier CAN read the public bundle,
        // because that is what their own login screen renders from.
        (await supplier.GetAsync("/api/v1/ui-strings/ar")).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/ui-strings/")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Removes an override when the scope ends - see the T-073 note above.</summary>
    private sealed class RevertUiString(HttpClient admin, string language, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await admin.DeleteAsync($"/api/v1/admin/ui-strings/{language}/{key}");
    }
}
