using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Api.Concurrency;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §8.1's concurrency contract, end to end (T3-34).
///
/// <para>These tests deliberately use <see cref="PostgresApiFixture.CreateRawClient"/> rather than
/// the suite's usual client: that client attaches a current <c>If-Match</c> to every mutation, which
/// is what lets three hundred pre-existing tests keep passing - and would make every assertion here
/// vacuous, because a caller that always sends a fresh version can never be missing one and can
/// never be stale.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ConcurrencyContractTests(PostgresApiFixture fixture)
{
    private static uint VersionFrom(HttpResponseMessage response)
    {
        response.Headers.ETag.Should().NotBeNull("§8.1: reads of a mutable aggregate return an ETag");
        ETag.TryParse(response.Headers.ETag!.ToString(), out var version).Should().BeTrue();
        return version;
    }

    private async Task<(HttpClient Client, string SupplierCode)> VerifiedSupplierAsync(string name)
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);
        var read = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        return (client, read.GetProperty("supplierCode").GetString()!);
    }

    // ---- the read half -------------------------------------------------------------------------

    [Fact]
    public async Task A_read_of_a_mutable_aggregate_returns_its_version_as_a_strong_ETag()
    {
        var (client, _) = await VerifiedSupplierAsync($"ETag Read {Guid.NewGuid():N}"[..30]);

        var response = await client.GetAsync("/api/v1/suppliers/me");

        var etag = response.Headers.ETag;
        etag.Should().NotBeNull();
        etag!.IsWeak.Should().BeFalse(
            "§8.1 calls it a strong ETag, and RFC 9110 §13.1.1 requires strong comparison for If-Match - " +
            "a weak validator could not be used as a precondition at all");
        VersionFrom(response).Should().NotBe(0u, "xmin is never zero for a real row");
    }

    [Fact]
    public async Task A_conditional_read_holding_the_current_version_gets_304_and_no_body()
    {
        var (client, _) = await VerifiedSupplierAsync($"NotModified {Guid.NewGuid():N}"[..30]);

        var first = await client.GetAsync("/api/v1/suppliers/me");
        var version = VersionFrom(first);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", ETag.Format(version));
        var second = await client.SendAsync(conditional);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_conditional_read_holding_a_different_version_gets_the_body()
    {
        // The control for the test above: 304 must depend on the version matching, not merely on the
        // header being present.
        var (client, _) = await VerifiedSupplierAsync($"NotModifiedCtl {Guid.NewGuid():N}"[..30]);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", ETag.Format(1u));
        var response = await client.SendAsync(conditional);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotBeEmpty();
    }

    // ---- the write half ------------------------------------------------------------------------

    [Fact]
    public async Task A_guarded_mutation_without_If_Match_is_refused_with_428()
    {
        var (etagClient, supplierCode) = await VerifiedSupplierAsync($"NoIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        var response = await raw.PatchAsJsonAsync($"/api/v1/suppliers/{supplierCode}",
            new { description = "no precondition", currencyCode = "SYP" });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, "§8.1: missing If-Match is 428");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IF_MATCH_REQUIRED");
        problem.GetProperty("type").GetString().Should().EndWith("/errors/precondition-required");
    }

    [Fact]
    public async Task The_same_mutation_WITH_If_Match_succeeds()
    {
        // The control. Without it, the 428 above would also pass against a route that was simply
        // broken, or one nobody is allowed to call.
        var (etagClient, supplierCode) = await VerifiedSupplierAsync($"WithIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        var read = await raw.GetAsync("/api/v1/suppliers/me");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = JsonContent.Create(new { description = "with precondition", currencyCode = "SYP" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ETag.Format(VersionFrom(read)));

        var response = await raw.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_star_If_Match_is_refused_rather_than_honoured()
    {
        // "*" is a legal If-Match under RFC 9110 and means "any current version" - which asserts
        // nothing about what the caller read, and is exactly the lost update the guard exists to
        // stop. Accepting it would leave a one-character bypass of the whole contract.
        var (etagClient, supplierCode) = await VerifiedSupplierAsync($"StarIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = JsonContent.Create(new { description = "wildcard", currencyCode = "SYP" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");

        var response = await raw.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("ETAG_MISMATCH");
    }

    [Fact]
    public async Task Creating_a_child_of_a_versioned_aggregate_IS_guarded_now()
    {
        // T-030 split (3) reversed what this test used to assert, and the reversal is the point.
        //
        // It read: "a creation POST is not one of §8.1's guarded mutations". That is true of a POST that
        // creates a top-level resource - there is no prior version to have read - but adding a CONTACT
        // creates a child of an existing Supplier, and the Supplier's version moves either way. Without
        // the precondition, a caller could add a contact on top of a profile they had never seen: one a
        // reviewer had just put back into InfoRequested, say, whose flagged-field rules they are unaware
        // of. So the create is a mutation OF the aggregate, and it is guarded.
        var (etagClient, _) = await VerifiedSupplierAsync($"GuardedCreate {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        var response = await raw.PostAsJsonAsync("/api/v1/suppliers/me/contacts",
            new { fullName = "Unguarded Contact", email = "unguarded@example.com", phone = "+963000000", role = "Ops" });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired,
            "a child of a versioned aggregate is a mutation of that aggregate");
    }

    [Fact]
    public async Task Creating_a_top_level_resource_still_needs_no_If_Match()
    {
        // The other direction of the gate, on a route where "creation" really means creation: an RFQ has
        // no prior version anyone could have read. If the filter were ever applied here by accident,
        // authoring would be impossible and only this test would say so.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = officer.DefaultRequestHeaders.Authorization;

        var response = await raw.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Unguarded creation RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddMinutes(5),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(7),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionRequired,
            "there is no version of a resource that does not exist yet");
    }

    [Fact]
    public async Task A_stray_If_Match_on_an_unguarded_mutation_does_not_gate_it()
    {
        // Only a route that declares the requirement participates. A header sent for some other resource
        // must not become a precondition nobody promised - it would fail a write nothing was contending,
        // which is worse than no guard.
        //
        // The route moved with split (3): /me/contacts now DECLARES the requirement, so a stale version
        // there is correctly a 412 and would prove the opposite of what this test is about. Resending
        // verification is a mutation on purpose - a POST with a real effect, and no version to have read.
        var (etagClient, _) = await VerifiedSupplierAsync($"StrayIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/resend-verification")
        {
            Content = JsonContent.Create(new { email = "stray@example.com" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ETag.Format(1u));

        var response = await raw.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionFailed);
        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionRequired);
    }

    // ---- the guard actually bites, per aggregate ------------------------------------------------

    /// <summary>
    /// The test that separates a real guard from a decorative one. A 428 only proves the filter
    /// runs; it says nothing about whether a WELL-FORMED but stale version is caught, which depends
    /// on the write reaching the versioned root at all. An endpoint that modifies only child rows
    /// would leave the root's xmin untouched, accept the stale version, and lose the update anyway -
    /// silently, and with a green 428 test alongside it.
    /// </summary>
    [Fact]
    public async Task A_stale_version_on_an_RFQ_edit_is_rejected_and_the_first_writer_survives()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, MotsSupplierPortal.Domain.Identity.Roles.ProcurementOfficer, org.Id);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, officer);

        var created = await raw.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Stale RFQ"));
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var read = await raw.GetAsync($"/api/v1/rfqs/{code}");
        var shared = VersionFrom(read);

        var first = await PutBasicsAsync(raw, code, "Written by A", shared);
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first writer holds a current version");

        var second = await PutBasicsAsync(raw, code, "Written by B", shared);
        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            "§8.1: the second writer's version is stale, so its precondition fails");
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("ETAG_MISMATCH");

        // The decisive assertion: A's write survived. Without a real guard this reads "Written by B".
        var after = await raw.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{code}");
        after.GetProperty("titleEn").GetString().Should().Be("Written by A",
            "the losing writer must not have overwritten the winner");
    }

    private static async Task<HttpResponseMessage> PutBasicsAsync(HttpClient client, string code, string titleEn, uint version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/rfqs/{code}")
        {
            Content = JsonContent.Create(RfqBasics(titleEn)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ETag.Format(version));
        return await client.SendAsync(request);
    }

    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = DateTimeOffset.UtcNow.AddDays(10),
        clarificationDeadlineAt = (DateTimeOffset?)null,
        evaluationTargetDate = (DateTimeOffset?)null,
    };
}
