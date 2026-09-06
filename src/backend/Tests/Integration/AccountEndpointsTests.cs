using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-902 and SCR-010: a user's own name, interface language, and the one-time record that they chose
/// it. Two screens over the same three columns, so one suite.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccountEndpointsTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task The_account_read_carries_what_the_screen_edits_and_nothing_that_decides_access()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Acct{Guid.NewGuid():N}"[..12]);

        var account = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");

        account.GetProperty("fullName").GetString().Should().NotBeNullOrWhiteSpace();
        account.GetProperty("email").GetString().Should().Contain("@");
        account.GetProperty("language").GetString().Should().Be("ar", "the registration default");

        // D-26: authorization data has ONE source, the access token's claims. A second copy here would
        // disagree with the token the moment a role changed mid-session, so its absence is the contract.
        account.TryGetProperty("permissions", out _).Should().BeFalse();
        account.TryGetProperty("roles", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_user_renames_themselves_and_switches_their_own_language()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Rename{Guid.NewGuid():N}"[..12]);

        var updated = await client.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "Layla H. Haddad", language = "en" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        // Asserted on the STORED row, not the echo: an endpoint that returns what it was sent while
        // writing nothing would pass an assertion against its own response.
        var read = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        read.GetProperty("fullName").GetString().Should().Be("Layla H. Haddad");
        read.GetProperty("language").GetString().Should().Be("en");
    }

    [Fact]
    public async Task A_language_the_product_does_not_ship_is_refused_and_one_it_ships_is_not()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Lang{Guid.NewGuid():N}"[..12]);

        var refused = await client.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "Nadia", language = "fr" });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an interface language with no strings to render is not a preference, it is a blank screen");

        // The guard both ways, so the refusal above is the value being checked rather than the route
        // refusing everything.
        var accepted = await client.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "Nadia", language = "en" });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        var blankName = await client.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "", language = "en" });
        blankName.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_unauthenticated_caller_reaches_neither_route()
    {
        var anonymous = fixture.CreateRawClient();

        (await anonymous.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "Nobody", language = "en" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/me/language", new { language = "en" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SCR_010_the_first_run_choice_is_recorded_once_and_keeps_its_first_timestamp()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"First{Guid.NewGuid():N}"[..12]);

        // The state a first-run chooser exists for: a stored default that nobody chose. The default is
        // "ar", so this flag is the ONLY thing that can distinguish a new user from one who picked
        // Arabic deliberately - which is why the column exists at all.
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        before.GetProperty("languageChosen").GetBoolean().Should().BeFalse();

        var chose = await client.PostAsJsonAsync("/api/v1/auth/me/language", new { language = "en" });
        chose.StatusCode.Should().Be(HttpStatusCode.OK, await chose.Content.ReadAsStringAsync());

        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        after.GetProperty("languageChosen").GetBoolean().Should().BeTrue();
        after.GetProperty("language").GetString().Should().Be("en");

        var email = after.GetProperty("email").GetString();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstStamp = await db.Users.AsNoTracking().Where(u => u.Email == email)
            .Select(u => u.LanguageChosenAt).FirstAsync();
        firstStamp.Should().NotBeNull();

        // Choosing again is still choosing, and the stamp keeps its original value: "when did this user
        // first decide" has to stay answerable, or the column records the last click instead of the
        // decision.
        (await client.PostAsJsonAsync("/api/v1/auth/me/language", new { language = "ar" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var secondStamp = await db.Users.AsNoTracking().Where(u => u.Email == email)
            .Select(u => u.LanguageChosenAt).FirstAsync();
        secondStamp.Should().Be(firstStamp);
    }

    [Fact]
    public async Task Saving_the_account_screen_also_counts_as_having_chosen()
    {
        // Otherwise a user who set their language in settings would still be asked the first-run question
        // - a screen asking something the user has just answered on another screen.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Counts{Guid.NewGuid():N}"[..12]);

        (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"))
            .GetProperty("languageChosen").GetBoolean().Should().BeFalse();

        await client.PutAsJsonAsync("/api/v1/auth/me", new { fullName = "Rami", language = "en" });

        (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"))
            .GetProperty("languageChosen").GetBoolean().Should().BeTrue();
    }
}
