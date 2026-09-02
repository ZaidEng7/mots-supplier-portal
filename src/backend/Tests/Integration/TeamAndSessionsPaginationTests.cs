using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-84: both lists here are business-bounded (a company's team, one person's active sessions)
/// rather than genuinely unbounded like the review queue, but the ticket scoped real pagination
/// for all four client-facing lists, so both get the same keyset treatment and the same
/// walk-the-pages standard of proof. Driven through the real HTTP endpoints (SupplierTestClient),
/// not direct handler DI resolution, because both handlers are scope-filtered (SupplierId/UserId
/// from JWT claims) and there is no fake IScopeContext in this test suite - PostgresApiFixture is
/// a real WebApplicationFactory with the real HttpScopeContext wired in.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class TeamAndSessionsPaginationTests(PostgresApiFixture fixture)
{
    private static async Task<List<T>> WalkAllAsync<T>(HttpClient client, string basePath)
    {
        var all = new List<T>();
        string? cursor = null;
        while (true)
        {
            var url = cursor is null ? $"{basePath}?pageSize=2" : $"{basePath}?pageSize=2&cursor={Uri.EscapeDataString(cursor)}";
            var res = await client.GetAsync(url);
            res.EnsureSuccessStatusCode();
            var page = await res.Content.ReadFromJsonAsync<JsonElement>();
            all.AddRange(page.GetProperty("data").Deserialize<List<T>>()!);
            if (!page.GetProperty("pagination").GetProperty("hasMore").GetBoolean()) break;
            cursor = page.GetProperty("pagination").GetProperty("nextCursor").GetString();
        }
        return all;
    }

    private sealed record TeamMemberRow(string userId, string email, string fullName, bool isActive);
    private sealed record SessionRow(string familyId, string? ip, string? userAgent, DateTimeOffset createdAt, DateTimeOffset expiresAt, bool isCurrent);

    private async Task AddTeamMemberAsync(Guid supplierId, string email)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = "Team Member",
            EmailConfirmed = true,
            IsActive = true,
            SupplierId = supplierId,
        };
        var created = await userManager.CreateAsync(user, SupplierTestClient.Password);
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
    }

    [Fact]
    public async Task Walking_all_pages_of_the_team_returns_every_member_exactly_once()
    {
        var (client, primaryEmail) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Team Walk Co");

        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var primaryUser = await userManager.FindByEmailAsync(primaryEmail);
            supplierId = primaryUser!.SupplierId!.Value;
        }

        // Scoped to a supplier this test alone created, so - unlike the review queue - an exact
        // total-count assertion is safe: nothing else in the shared database can add a row here.
        var extraEmails = new[] { "b1", "b2", "b3", "b4" }.Select(p => $"{p}-{Guid.NewGuid():N}@example.com").ToList();
        foreach (var email in extraEmails) await AddTeamMemberAsync(supplierId, email);

        var walked = await WalkAllAsync<TeamMemberRow>(client, "/api/v1/suppliers/me/users/");
        var walkedEmails = walked.Select(w => w.email).ToList();

        walkedEmails.Should().OnlyHaveUniqueItems();
        walkedEmails.Should().BeEquivalentTo([primaryEmail, .. extraEmails]);
    }

    [Fact]
    public async Task A_team_member_invited_between_page_fetches_is_picked_up_without_disturbing_the_walk()
    {
        // No operation in this app removes a user from this list (disabling a user does not
        // exclude them - ListSupplierUsersHandler returns active and inactive rows alike), so a
        // "member removed mid-walk" boundary is not a realistic scenario here. Invitation mid-walk
        // is realistic, and is the case worth proving: a keyset cursor only ever asks "what comes
        // after the last key I saw", so a new row landing AFTER that key must appear on the very
        // next fetch, and one landing before it (already "passed") correctly does not reappear.
        var (client, primaryEmail) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Team Insert Co");

        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            supplierId = (await userManager.FindByEmailAsync(primaryEmail))!.SupplierId!.Value;
        }

        // Sort order is by email ascending. "aa-"/"bb-" sort before "itest-" (the primary's
        // auto-generated prefix); "zz-" sorts after it.
        var aa = $"aa-{Guid.NewGuid():N}@example.com";
        var bb = $"bb-{Guid.NewGuid():N}@example.com";
        var zz1 = $"zz1-{Guid.NewGuid():N}@example.com";
        await AddTeamMemberAsync(supplierId, aa);
        await AddTeamMemberAsync(supplierId, bb);
        // primaryEmail ("itest-...") sorts between bb and zz1.

        var page1Res = await client.GetAsync("/api/v1/suppliers/me/users/?pageSize=2");
        var page1 = await page1Res.Content.ReadFromJsonAsync<JsonElement>();
        page1.GetProperty("data").Deserialize<List<TeamMemberRow>>()!.Select(r => r.email)
            .Should().BeEquivalentTo([aa, bb], options => options.WithStrictOrdering());
        var cursor1 = page1.GetProperty("pagination").GetProperty("nextCursor").GetString();

        // Insert zz1 between page 1 and page 2 - it sorts after "bb-" (the cursor position), so a
        // forward keyset walk started before this insert must still see it.
        await AddTeamMemberAsync(supplierId, zz1);

        var page2Res = await client.GetAsync($"/api/v1/suppliers/me/users/?pageSize=2&cursor={Uri.EscapeDataString(cursor1!)}");
        var page2 = await page2Res.Content.ReadFromJsonAsync<JsonElement>();
        page2.GetProperty("data").Deserialize<List<TeamMemberRow>>()!.Select(r => r.email)
            .Should().BeEquivalentTo([primaryEmail, zz1], options => options.WithStrictOrdering(),
                "zz1 was inserted after the page-1 cursor position, so the walk must reach it");
        page2.GetProperty("pagination").GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    private async Task<Guid> AddSessionAsync(Guid userId, DateTimeOffset createdAt)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var familyId = Guid.NewGuid();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = $"faketoken-{Guid.NewGuid():N}",
            FamilyId = familyId,
            CreatedAt = createdAt,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            RevokedAt = null,
        });
        await db.SaveChangesAsync();
        return familyId;
    }

    [Fact]
    public async Task Walking_all_pages_of_sessions_returns_every_active_session_exactly_once()
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Sessions Walk Co");

        Guid userId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            userId = (await userManager.FindByEmailAsync(email))!.Id;
        }

        // The login above already created one real session. Four more, explicitly older, so
        // ordering (newest-first) is deterministic rather than racing wall-clock precision.
        var now = DateTimeOffset.UtcNow;
        var extraFamilyIds = new List<Guid>();
        for (var i = 1; i <= 4; i++)
        {
            extraFamilyIds.Add(await AddSessionAsync(userId, now.AddMinutes(-i)));
        }

        var walked = await WalkAllAsync<SessionRow>(client, "/api/v1/auth/sessions");
        var walkedFamilyIds = walked.Select(w => Guid.Parse(w.familyId)).ToList();

        walkedFamilyIds.Should().OnlyHaveUniqueItems();
        walkedFamilyIds.Should().HaveCount(5, "the real login session plus the four synthetic ones");
        foreach (var familyId in extraFamilyIds)
        {
            walkedFamilyIds.Should().Contain(familyId);
        }
    }

    [Fact]
    public async Task A_session_revoked_between_page_fetches_does_not_skip_the_sessions_that_remain()
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, "Sessions Revoke Co");

        Guid userId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            userId = (await userManager.FindByEmailAsync(email))!.Id;
        }

        var now = DateTimeOffset.UtcNow;
        // Newest to oldest: real login session (~now), s2 (-1min), s3 (-2min), s4 (-3min).
        var s2 = await AddSessionAsync(userId, now.AddMinutes(-1));
        var s3 = await AddSessionAsync(userId, now.AddMinutes(-2));
        var s4 = await AddSessionAsync(userId, now.AddMinutes(-3));

        var page1Res = await client.GetAsync("/api/v1/auth/sessions?pageSize=2");
        var page1 = await page1Res.Content.ReadFromJsonAsync<JsonElement>();
        var page1Ids = page1.GetProperty("data").Deserialize<List<SessionRow>>()!.Select(r => Guid.Parse(r.familyId)).ToList();
        page1Ids.Should().HaveCount(2);
        page1Ids.Should().Contain(s2, "s2 is the second-newest session and must be on page 1");
        var cursor1 = page1.GetProperty("pagination").GetProperty("nextCursor").GetString();

        // Revoke the already-fetched, most-recent session (the real login one) between fetches -
        // the shape that shifts an offset-based page 2 and drops the row right after the boundary.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newest = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstAsync();
            newest.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var page2Res = await client.GetAsync($"/api/v1/auth/sessions?pageSize=2&cursor={Uri.EscapeDataString(cursor1!)}");
        var page2 = await page2Res.Content.ReadFromJsonAsync<JsonElement>();
        var page2Ids = page2.GetProperty("data").Deserialize<List<SessionRow>>()!.Select(r => Guid.Parse(r.familyId)).ToList();
        page2Ids.Should().BeEquivalentTo([s3, s4],
            "s3 must not be skipped and s4 must not be duplicated because the newest session left the list");
    }
}
