using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// An authenticated read must not be reusable from a cache without asking the server first.
///
/// <para><b>What went wrong.</b> Every GET outside four path prefixes answered with an ETag and NO
/// <c>Cache-Control</c>. RFC 9111 §4.2.2 lets a cache assign its own freshness to exactly that
/// combination - a stored response carrying a validator and no explicit lifetime - and a browser keys
/// the entry on the URL. <c>Authorization</c> is not part of that key unless <c>Vary</c> names it, and
/// nothing did. Two people signing into the same browser therefore shared one entry per URL.</para>
///
/// <para>Found by a supplier who registered a new account, opened "Complete your supplier profile",
/// and read the PREVIOUS account's legal name, registration number and Approved state. Reproduced to
/// the byte: a plain <c>fetch</c> of <c>/api/v1/suppliers/me</c> returned the earlier user's body while
/// the same request with <c>cache: 'no-store'</c> returned the correct one. The server was never asked.</para>
///
/// <para><b>Why no-cache and not no-store.</b> §8.1 builds conditional reads on these ETags, so
/// <c>no-store</c> would delete a documented feature to fix this. <c>no-cache</c> keeps the stored copy
/// and forbids using it without revalidation, which puts the token check back in front of every reuse.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CacheControlHeaderTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// The denominator. The middleware decides by path prefix, so the rule only covers the ETag-emitting
    /// reads if every one of them is under <c>/api/v1/</c>. A read mapped anywhere else would be exempt
    /// without anybody choosing that - the same silent-exemption shape the If-Match sweep guards against.
    /// </summary>
    [Fact]
    public void Every_etag_emitting_read_sits_under_the_prefix_the_rule_covers()
    {
        var etagReads = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<EmitsETagMetadata>() is not null)
            .Select(e => e.RoutePattern.RawText ?? "")
            .ToList();

        etagReads.Should().HaveCountGreaterThan(10,
            "an empty or tiny set would pass the assertion below while checking nothing");

        etagReads.Where(p => !p.TrimStart('/').StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the cache rule is applied by path prefix, so a read outside it is uncovered");
    }

    [Fact]
    public async Task A_suppliers_own_profile_cannot_be_reused_from_a_cache_without_revalidating()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Cache {Guid.NewGuid():N}"[..30]);

        var response = await client.GetAsync("/api/v1/suppliers/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull("this is the validator that made heuristic caching legal");
        // Asserted on the parsed directives rather than the header's text: HttpClient reorders them
        // ("no-cache, private"), and an assertion that a reordering can break is an assertion about
        // string formatting, not about caching.
        response.Headers.CacheControl!.NoCache.Should().BeTrue("a stored copy may not be reused without asking");
        response.Headers.CacheControl!.Private.Should().BeTrue("no shared cache may store an authenticated body");
        response.Headers.Vary.Should().Contain("Authorization",
            "the browser keys a cache entry by URL; only Vary makes the bearer token part of that key");
    }

    /// <summary>
    /// Two suppliers reading their own profile must not be handed the same entity-tag.
    ///
    /// <para>They were. The tag encoded a row version and a build, so every resource sitting at version 3
    /// carried the identical string. That is what turned a cache entry into a disclosure: the second
    /// supplier's conditional read sent the first one's tag, it matched the second one's own row version,
    /// the server answered 304, and the browser served the first supplier's body.</para>
    ///
    /// <para>The arrangement is the control. Both suppliers are created the same way, so both sit at the
    /// same row version - which is precisely the case the old tag could not tell apart, and a test that
    /// did not force it would pass on a coincidence of numbers.</para>
    /// </summary>
    [Fact]
    public async Task Two_suppliers_at_the_same_row_version_do_not_share_an_entity_tag()
    {
        var (first, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"TagA {Guid.NewGuid():N}"[..30]);
        var (second, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"TagB {Guid.NewGuid():N}"[..30]);

        var firstRead = await first.GetAsync("/api/v1/suppliers/me");
        var secondRead = await second.GetAsync("/api/v1/suppliers/me");

        var firstBody = await firstRead.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await secondRead.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("rowVersion").GetInt64()
            .Should().Be(secondBody.GetProperty("rowVersion").GetInt64(),
                "the control: these two must collide on version, or the tags differ for the wrong reason");

        firstRead.Headers.ETag!.Tag.Should().NotBe(secondRead.Headers.ETag!.Tag,
            "an entity-tag identifies a representation, and these are two different suppliers' profiles");

        // And the consequence, end to end: the second supplier's conditional read carrying the FIRST
        // supplier's tag must be answered with their own body, never a 304 that would leave the first
        // supplier's profile on screen.
        second.DefaultRequestHeaders.IfNoneMatch.ParseAdd(firstRead.Headers.ETag!.Tag);
        var conditional = await second.GetAsync("/api/v1/suppliers/me");

        conditional.StatusCode.Should().Be(HttpStatusCode.OK, "someone else's tag is not evidence about this resource");
        (await conditional.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("supplierCode").GetString()
            .Should().Be(secondBody.GetProperty("supplierCode").GetString());
    }

    /// <summary>
    /// The four families that were already <c>no-store</c> keep it. They are stricter than the rule above
    /// on purpose - a download URL and a reviewer's queue should not sit in a disk cache at all - so a
    /// change that made everything uniform would be a downgrade for these.
    /// </summary>
    [Fact]
    public async Task The_paths_that_were_already_no_store_still_are()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var queue = await reviewer.GetAsync("/api/v1/review/queue?pageSize=1");

        queue.StatusCode.Should().Be(HttpStatusCode.OK);
        queue.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    /// <summary>
    /// A refusal is a response too. The header set is applied before authorization runs, so a 401 carries
    /// it as well - which matters, because a cached 401 served to the next person is its own bug.
    /// </summary>
    [Fact]
    public async Task An_unauthenticated_refusal_carries_the_same_rule()
    {
        var anonymous = fixture.CreateClient();

        var refused = await anonymous.GetAsync("/api/v1/suppliers/me");

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.CacheControl!.NoCache.Should().BeTrue();
        refused.Headers.CacheControl!.Private.Should().BeTrue();
    }
}
