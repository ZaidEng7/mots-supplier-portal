using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-030 split (3): the supplier's own child writes carry §8.1's precondition.
///
/// <para>Split (1) made every child write MOVE the Supplier's version. It did not make any of them GUARD
/// it, so two of a supplier's users editing different contacts on the same profile both won and the loser
/// was never told - MSP-65's "decoration", one level down. These twenty-one routes now require If-Match.</para>
///
/// <para>The client used here is the RAW one. Every other suite goes through <c>ETagAttachingHandler</c>,
/// which probes for a fresh ETag before each mutation - useful everywhere else and useless here, because
/// it would make the missing-precondition case impossible to observe.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierChildWriteConcurrencyTests(PostgresApiFixture fixture)
{
    private static async Task<(HttpClient Raw, string SupplierCode)> RawSupplierAsync(PostgresApiFixture fixture, string name)
    {
        // The authenticated client the fixture builds, minus the ETag-probing handler: this suite is about
        // what happens when the header is absent, stale, or exactly right.
        var withHandler = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);
        // CreateRawClient, not CreateClient: the fixture puts ETagAttachingHandler on every client it
        // makes, and that handler probes for a fresh ETag before each mutation - which would attach the
        // very header the first test below is trying to observe the absence of.
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;

        // R-9 renamed this to supplierCode; the code itself is not used by these tests, but reading it
        // proves the raw client is authenticated before anything else is asserted.
        var me = await raw.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        return (raw, me.GetProperty("supplierCode").GetString()!);
    }

    private static async Task<string> CurrentETagAsync(HttpClient raw)
    {
        var response = await raw.GetAsync("/api/v1/suppliers/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull("GET /suppliers/me must issue the precondition these writes require");
        return response.Headers.ETag!.ToString();
    }

    private static HttpRequestMessage Post(string path, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    private static object Contact(string suffix) => new
    {
        fullName = $"Contact {suffix}",
        email = $"contact-{suffix}@example.com",
        phone = "+963900000000",
        role = "Procurement",
    };

    [Fact]
    public async Task A_child_write_without_If_Match_is_refused_and_with_it_succeeds()
    {
        var (raw, _) = await RawSupplierAsync(fixture, $"ChildGuard {Guid.NewGuid():N}"[..24]);

        // Refused, and refused with the status §8.1 names: 428, not 400 - the request is well-formed and
        // the precondition is what is missing.
        var unguarded = await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("a"), ifMatch: null));
        unguarded.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, await unguarded.Content.ReadAsStringAsync());

        // The control: the same write with the version the read issued.
        var guarded = await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("b"), await CurrentETagAsync(raw)));
        guarded.StatusCode.Should().Be(HttpStatusCode.OK, await guarded.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stale_If_Match_is_refused_with_412()
    {
        var (raw, _) = await RawSupplierAsync(fixture, $"ChildStale {Guid.NewGuid():N}"[..24]);
        var original = await CurrentETagAsync(raw);

        // One successful write moves the version on, which is what split (1) built.
        (await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("first"), original)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Replaying the version from BEFORE that write is the lost update this guard exists to stop.
        var stale = await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("second"), original));
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, await stale.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_child_write_returns_the_version_it_produced_so_the_next_edit_needs_no_re_read()
    {
        // The reason WithFreshETag exists. The SPA drops its cached version the moment a mutation succeeds
        // - a kept one would be stale by definition - so without a fresh ETag on the response a supplier
        // editing two contacts in a row would hit 428 on the second until a re-read landed. This asserts
        // the sequence a person actually performs: read once, then edit twice.
        var (raw, _) = await RawSupplierAsync(fixture, $"ChildChain {Guid.NewGuid():N}"[..24]);

        var first = await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("chain-1"), await CurrentETagAsync(raw)));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull("the write returns the version it produced");

        var second = await raw.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("chain-2"), first.Headers.ETag!.ToString()));
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        second.Headers.ETag!.ToString().Should().NotBe(first.Headers.ETag!.ToString(), "and each write moves it again");
    }

    [Fact]
    public async Task Two_users_of_one_supplier_editing_different_children_do_not_both_win()
    {
        // The defect in one test. Both callers read the same version; the second edits a DIFFERENT child,
        // so nothing they touch overlaps - and before this split both writes succeeded, because the guard
        // was looking for a Modified ROOT that no child write produced.
        var withHandler = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"TwoUsers {Guid.NewGuid():N}"[..24]);
        var alice = fixture.CreateRawClient();
        alice.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;
        var bob = fixture.CreateRawClient();
        bob.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;

        var shared = await CurrentETagAsync(alice);

        (await alice.SendAsync(Post("/api/v1/suppliers/me/contacts", Contact("alice"), shared)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Bob's version is now stale even though he is editing an address rather than a contact: the
        // aggregate moved, which is the whole point of an aggregate-level version.
        var bobsWrite = await bob.SendAsync(Post("/api/v1/suppliers/me/addresses", new
        {
            kind = "Branch", line1 = "Bob Street", line2 = (string?)null, city = "Damascus",
            regionCode = "DM", country = "SY", postalCode = (string?)null,
            latitude = (decimal?)null, longitude = (decimal?)null,
        }, shared));

        bobsWrite.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "one aggregate, one version - a child write by anyone invalidates a version held by everyone else");
    }
}
