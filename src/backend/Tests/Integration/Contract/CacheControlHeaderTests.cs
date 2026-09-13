// An authenticated read must not be reusable from a cache without asking the server first.
//
//
// WHAT WENT WRONG
//
// Every read outside four path prefixes answered with a validator and NO caching directive.
//
// The standard lets a cache assign its own freshness to exactly that combination, a stored response carrying a
// validator and no explicit lifetime, and a browser keys the entry on the address. The authorisation header is not
// part of that key unless the response says it is, and nothing did.
//
// So two people signing into the same browser shared one entry per address.
//
// Found by a supplier who registered a new account, opened their profile, and read the PREVIOUS account's legal
// name, registration number and approved state. Reproduced to the byte: a plain fetch returned the earlier user's
// body while the same request with caching disabled returned the correct one. The server was never asked.
//
//
// WHY REVALIDATION RATHER THAN NO STORAGE AT ALL
//
// Conditional reads are built on these validators, so forbidding storage would delete a documented feature to fix
// this.
//
// Requiring revalidation keeps the stored copy and forbids using it without asking, which puts the token check back
// in front of every reuse.
//
//
// THE SECOND HALF: TWO SUPPLIERS MUST NOT SHARE AN ENTITY TAG
//
// They did. The tag encoded a row version and a build, so every resource sitting at the same version carried the
// identical string.
//
// That is what turned a cache entry into a disclosure: the second supplier's conditional read sent the first one's
// tag, it matched their own row version, the server answered not-modified, and the browser served the first
// supplier's body.
//
// The arrangement IS the control. Both suppliers are created the same way, so both sit at the same row version,
// which is precisely the case the old tag could not tell apart. A test that did not force it would pass on a
// coincidence of numbers.
//
// And the consequence is asserted end to end: the second supplier's conditional read carrying the first one's tag
// must be answered with their own body, never a not-modified that would leave the first supplier's profile on
// screen.
//
//
// THE DENOMINATOR, AND TWO DELIBERATE EXCEPTIONS
//
// The middleware decides by path prefix, so the rule only covers the validator-emitting reads if every one of them
// is under the versioned prefix. A read mapped anywhere else would be exempt without anybody choosing that, which
// is the same silent-exemption shape the precondition sweep guards against.
//
// The four families that already forbade storage keep it. They are stricter than the general rule on purpose, since
// a download link and a reviewer's queue should not sit in a disk cache at all, so a change that made everything
// uniform would be a downgrade for them.
//
// And a refusal is a response too: the headers are applied before authorisation runs, so an unauthenticated
// response carries them as well, which matters because a cached refusal served to the next person is its own bug.
//
// The directives are asserted parsed rather than as header text, because the client reorders them and an assertion
// a reordering can break is an assertion about string formatting rather than about caching.

namespace MotsSupplierPortal.Tests.Integration.Contract;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CacheControlHeaderTests(PostgresApiFixture fixture)
{
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
        response.Headers.CacheControl!.NoCache.Should().BeTrue("a stored copy may not be reused without asking");
        response.Headers.CacheControl!.Private.Should().BeTrue("no shared cache may store an authenticated body");
        response.Headers.Vary.Should().Contain("Authorization",
            "the browser keys a cache entry by URL; only Vary makes the bearer token part of that key");
    }

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

        second.DefaultRequestHeaders.IfNoneMatch.ParseAdd(firstRead.Headers.ETag!.Tag);
        var conditional = await second.GetAsync("/api/v1/suppliers/me");

        conditional.StatusCode.Should().Be(HttpStatusCode.OK, "someone else's tag is not evidence about this resource");
        (await conditional.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("supplierCode").GetString()
            .Should().Be(secondBody.GetProperty("supplierCode").GetString());
    }

    [Fact]
    public async Task The_paths_that_were_already_no_store_still_are()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var queue = await reviewer.GetAsync("/api/v1/review/queue?pageSize=1");

        queue.StatusCode.Should().Be(HttpStatusCode.OK);
        queue.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

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
