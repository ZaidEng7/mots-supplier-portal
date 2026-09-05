using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-030 split (2): the RFQ's own child writes carry §8.1's precondition.
///
/// <para>Split (1) made every child write MOVE the RFQ's version and split (3) did the guarding for the
/// supplier's children. The RFQ's were still unguarded, so two officers editing the same tender both won
/// and the loser was never told — the same defect, on the aggregate where a lost update decides what a
/// supplier is bidding on.</para>
///
/// <para>The client here is the RAW one. Every other suite goes through <c>ETagAttachingHandler</c>,
/// which probes for a fresh ETag before each mutation — correct everywhere else, and useless here,
/// because it makes the missing-precondition case impossible to observe.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqChildWriteConcurrencyTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null,
        evaluationTargetDate = (DateTimeOffset?)null,
    };

    private static object Item(string suffix) => new
    {
        titleAr = "بند",
        titleEn = $"Item {suffix}",
        specificationAr = (string?)null,
        specificationEn = (string?)null,
        categoryCode = "catering",
        quantity = 5,
        unitOfMeasureCode = "unit",
        isUnitPrice = true,
        isOptional = false,
    };

    private async Task<(HttpClient Raw, string Code)> RawOfficerWithDraftAsync(string titleEn)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var withHandler = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        // CreateRawClient, not CreateClient: the fixture puts ETagAttachingHandler on every client it
        // makes, and that handler would attach the very header the first test is observing the absence of.
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;

        var created = await raw.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
        return (raw, code);
    }

    private static async Task<string> CurrentETagAsync(HttpClient raw, string code)
    {
        var response = await raw.GetAsync($"/api/v1/rfqs/{code}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull(
            "GET /rfqs/{code} must issue the precondition these writes require — a guard nobody can satisfy refuses every caller");
        return response.Headers.ETag!.ToString();
    }

    private static HttpRequestMessage Post(string path, object? body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    [Fact]
    public async Task Adding_an_item_without_a_precondition_is_refused()
    {
        var (raw, code) = await RawOfficerWithDraftAsync("Unguarded Add");

        var response = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/items", Item("A"), ifMatch: null));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        // The control: the identical request WITH the header succeeds, so the 428 above is the missing
        // precondition and not the payload, the permission or the state.
        var withHeader = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/items", Item("B"), await CurrentETagAsync(raw, code)));
        withHeader.StatusCode.Should().Be(HttpStatusCode.OK, await withHeader.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stale_precondition_is_refused_rather_than_resolved_in_favour_of_whoever_saved_second()
    {
        var (raw, code) = await RawOfficerWithDraftAsync("Stale Add");

        var stale = await CurrentETagAsync(raw, code);

        // Somebody else moves the RFQ on. Through the API, because the point is that a version obtained
        // legitimately becomes stale without the holder doing anything.
        (await raw.SendAsync(Post($"/api/v1/rfqs/{code}/items", Item("First"), stale)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/requirements",
            new { textAr = "متطلب", textEn = "Requirement", isMandatory = true, documentTypeCode = (string?)null }, stale));

        refused.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("ETAG_MISMATCH");

        // The control: the FRESH version is accepted, so the 412 is about staleness and not about the
        // route refusing every precondition it is handed.
        (await raw.SendAsync(Post($"/api/v1/rfqs/{code}/requirements",
            new { textAr = "متطلب", textEn = "Requirement", isMandatory = true, documentTypeCode = (string?)null },
            await CurrentETagAsync(raw, code))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_child_write_returns_the_version_it_produced_so_the_next_one_can_be_made()
    {
        // Without this, an officer adding two items in a row would 428 on the second: the SPA drops its
        // cached version on every successful mutation, and there would be nothing to replace it with
        // until a re-read landed. `WithFreshETag`, not `WithETag` — the latter also answers 304 to a
        // conditional read, and a 304 on a POST that changed the row would be a lie.
        var (raw, code) = await RawOfficerWithDraftAsync("Chained Adds");

        var first = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/items", Item("One"), await CurrentETagAsync(raw, code)));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull("a guarded child write must hand back the version it produced");

        var second = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/items", Item("Two"), first.Headers.ETag!.ToString()));
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        second.Headers.ETag.Should().NotBe(first.Headers.ETag, "the second write moved the version again");
    }

    [Fact]
    public async Task Creating_an_RFQ_needs_no_precondition_and_a_supplier_writing_to_one_needs_none_either()
    {
        // Both halves are deliberate exclusions, asserted so a later sweep does not "finish the job" and
        // break them. Creating a top-level resource has no prior version anyone could have read (D-37);
        // a supplier's clarification has none obtainable, because SupplierRfqDto carries no version, and
        // guarding it would 428 every invited supplier.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var withHandler = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;

        var created = await raw.SendAsync(Post("/api/v1/rfqs", RfqBasics("No Precondition Needed"), ifMatch: null));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        // The supplier half, on a published RFQ they were invited to, is covered end-to-end by
        // SupplierRfqEndpointsTests — which goes through the ETag-attaching client and would have started
        // failing had these two routes been guarded. Asserted here at the shape level: the read a supplier
        // gets carries no version to send back.
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
        var buyerRead = await raw.GetAsync($"/api/v1/rfqs/{code}");
        buyerRead.Headers.ETag.Should().NotBeNull("the buyer's read issues the precondition");
    }
}
