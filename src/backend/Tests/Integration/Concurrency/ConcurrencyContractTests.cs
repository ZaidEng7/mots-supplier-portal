// §8.1's concurrency contract, end to end (T3-34). Three groups of tests in this order: the read half, the
// write half, and whether the guard actually bites per aggregate.
//
// These deliberately use PostgresApiFixture.CreateRawClient rather than the suite's usual client. That
// client attaches a current If-Match to every mutation, which is what lets three hundred pre-existing tests
// keep passing - and would make every assertion here vacuous, because a caller that always sends a fresh
// version can never be missing one and can never be stale.
//
// THE READ HALF. §8.1's tag identifies the REPRESENTATION, not only the row, so it has to change when the
// response's shape does. The tag encoded the row version and nothing else, so adding a field to a DTO
// changed no tag: after a deploy, a client holding a cached body for an unchanged row kept that body and
// the new field was invisible to it. Found while verifying a one-line addition - the API returned the new
// field to curl and the browser rendered the old shape, because its cached body still matched. The build
// discriminator is what makes that stop. The version half must still be readable, because If-Match depends
// on it: a client that read before a deploy and writes after it is making a legitimate claim about the row,
// so a tag from an older build on the same row still parses to the same version and If-Match keeps working
// across a deployment rather than answering 412 over a suffix.
//
// Then §8.1's BROWSER half: the version a read returns has to be readable by the script that will send it
// back. Every other test in this file reads response.Headers.ETag from an HttpClient, which sees every
// header on the wire. A browser does not - on a cross-origin response, script gets the CORS-safelisted
// headers and nothing else, and ETag is not safelisted. So the whole concurrency layer could pass this
// suite while being invisible to the SPA, which is exactly what it was doing until this test existed.
// Reproduced in the browser first: clicking Save on a seeded draft proposal logged "[concurrency] PATCH ...
// was refused for a missing If-Match" and the API answered 428. The test is about the CORS response header
// rather than about any endpoint, so it uses the cheapest authenticated read that returns an ETag. Its
// control is that a same-origin request - no Origin at all - carries no exposure header, because nothing is
// being hidden from script in the first place; and the origin has to be an ALLOWED one, since an unknown
// origin gets no CORS headers at all, which is the policy refusing rather than the exposure being
// unconditional.
//
// The 304 test echoes back the tag the server actually issued, which is what a client does and what the
// header means. It is no longer reconstructible from the version alone: a tag now identifies the resource
// and the caller as well, so that one supplier's validator cannot speak for another's. Its control changes
// ONLY the version half of the server's own tag, leaving the build and resource halves matching exactly - a
// tag invented from scratch would differ in three ways at once and could pass the control while the version
// comparison was broken.
//
// THE WRITE HALF. A guarded write with no precondition is refused, with a control: without it the 428 would
// also pass against a route that was simply broken, or one nobody is allowed to call. "*" is refused too -
// it is a legal If-Match under RFC 9110 meaning "any current version", which asserts nothing about what the
// caller read and is exactly the lost update the guard exists to stop; accepting it would leave a
// one-character bypass of the whole contract.
//
// The contact-creation test reverses what it used to assert, and the reversal is the point. It read: "a
// creation POST is not one of §8.1's guarded mutations". That is true of a POST that creates a top-level
// resource - there is no prior version to have read - but adding a CONTACT creates a child of an existing
// Supplier, and the Supplier's version moves either way. Without the precondition a caller could add a
// contact on top of a profile they had never seen: one a reviewer had just put back into InfoRequested, say,
// whose flagged-field rules they are unaware of. So the create is a mutation OF the aggregate, and T-030
// split (3) guards it. The other direction of the gate is a route where "creation" really means creation:
// an RFQ has no prior version anyone could have read, and if the filter were ever applied there by accident
// authoring would be impossible and only that test would say so.
//
// Only a route that declares the requirement participates. A header sent for some other resource must not
// become a precondition nobody promised - it would fail a write nothing was contending, which is worse than
// no guard. That test's route moved with split (3): /me/contacts now DECLARES the requirement, so a stale
// version there is correctly a 412 and would prove the opposite of what the test is about. Resending
// verification is a mutation on purpose - a POST with a real effect, and no version to have read.
//
// THE GUARD ACTUALLY BITES. The last test is the one that separates a real guard from a decorative one. A
// 428 only proves the filter runs; it says nothing about whether a WELL-FORMED but stale version is caught,
// which depends on the write reaching the versioned root at all. An endpoint that modifies only child rows
// would leave the root's xmin untouched, accept the stale version, and lose the update anyway - silently,
// and with a green 428 test alongside it. The decisive assertion is that A's write survived: without a real
// guard it reads "Written by B".

namespace MotsSupplierPortal.Tests.Integration.Concurrency;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Tests.Integration;

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

    [Fact]
    public async Task An_entity_tag_carries_the_build_as_well_as_the_row_version()
    {
        var (client, _) = await VerifiedSupplierAsync($"Build{Guid.NewGuid():N}"[..12]);

        var read = await client.GetAsync("/api/v1/suppliers/me");
        var tag = read.Headers.ETag!.ToString();

        tag.Trim('"').Should().Contain(".", "the tag is <version>.<build>; without the build half a cached body " +
            "survives a deploy that changed the response's shape");

        ETag.TryParse(tag, out var version).Should().BeTrue();
        version.Should().BeGreaterThan(0u);

        var withoutBuild = $"\"{tag.Trim('"').Split('.')[0]}\"";
        ETag.TryParse(withoutBuild, out var legacyVersion).Should().BeTrue();
        legacyVersion.Should().Be(version);
    }

    [Fact]
    public async Task A_cross_origin_read_exposes_its_ETag_to_script()
    {
        var (client, supplierCode) = await VerifiedSupplierAsync($"Expose{Guid.NewGuid():N}"[..12]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        request.Headers.Add("Origin", "http://localhost:5173");
        var response = await client.SendAsync(request);

        response.Headers.ETag.Should().NotBeNull("the header is on the wire either way");
        response.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed).Should().BeTrue(
            "without this header a browser hides ETag from script and every guarded write goes out with no If-Match");
        string.Join(",", exposed!).Should().Contain("ETag");

        var sameOrigin = await client.GetAsync("/api/v1/suppliers/me");
        sameOrigin.Headers.TryGetValues("Access-Control-Expose-Headers", out _).Should().BeFalse();

        var foreign = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/suppliers/{supplierCode}");
        foreign.Headers.Add("Origin", "https://not-the-spa.example");
        (await client.SendAsync(foreign)).Headers
            .TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse();
    }

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

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag!.Tag);
        var second = await client.SendAsync(conditional);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_conditional_read_holding_a_different_version_gets_the_body()
    {
        var (client, _) = await VerifiedSupplierAsync($"NotModifiedCtl {Guid.NewGuid():N}"[..30]);

        var real = (await client.GetAsync("/api/v1/suppliers/me")).Headers.ETag!.Tag.Trim('"');
        var halves = real.Split('.', 2);
        var wrongVersion = $"\"{(halves[0] == "AAAAAQ" ? "AAAAAg" : "AAAAAQ")}.{halves[1]}\"";

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/suppliers/me");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", wrongVersion);
        var response = await client.SendAsync(conditional);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotBeEmpty();
    }

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
        var (etagClient, supplierCode) = await VerifiedSupplierAsync($"WithIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        var read = await raw.GetAsync("/api/v1/suppliers/me");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{supplierCode}")
        {
            Content = JsonContent.Create(new { description = "with precondition", currencyCode = "SYP" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ETag.ForPrecondition(VersionFrom(read)));

        var response = await raw.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_star_If_Match_is_refused_rather_than_honoured()
    {
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
        var (etagClient, _) = await VerifiedSupplierAsync($"StrayIfMatch {Guid.NewGuid():N}"[..30]);
        var raw = await SupplierTestClient.CloneWithoutETagsAsync(fixture, etagClient);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/resend-verification")
        {
            Content = JsonContent.Create(new { email = "stray@example.com" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ETag.ForPrecondition(1u));

        var response = await raw.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionFailed);
        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionRequired);
    }

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
        request.Headers.TryAddWithoutValidation("If-Match", ETag.ForPrecondition(version));
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
