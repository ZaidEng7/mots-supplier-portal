using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Validation runs where it used to run: after the permission check and after the write precondition.
///
/// <para>Moving it out of the handlers and onto the routes moved WHEN it runs. Filters run in the order
/// they are declared, so declaring it before the permission would tell a caller which fields are wrong on
/// a route they may not call, and declaring it before the precondition would answer a validation failure
/// where a caller used to be told their precondition was missing.</para>
///
/// <para>Nothing about getting that wrong fails to compile, which is why these exist. Each test sends a
/// body that is invalid on purpose, so the only thing under test is which refusal wins.</para>
///
/// <para>The client is the RAW one. Every other suite goes through the handler that attaches a fresh
/// precondition before each write, which would make the precondition case impossible to observe.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ValidationFilterOrderTests(PostgresApiFixture fixture)
{
    // Invalid on purpose: the validator requires non-empty text in both languages.
    private static object BlankRequirement() => new
    {
        textAr = "",
        textEn = "",
        isMandatory = true,
        documentTypeCode = (string?)null,
    };

    private static object RfqBasics() => new
    {
        titleAr = "طلب اختبار",
        titleEn = "Validation Order",
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null,
        evaluationTargetDate = (DateTimeOffset?)null,
    };

    private static HttpRequestMessage Post(string path, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    private async Task<(HttpClient Raw, string Code)> OfficerWithDraftAsync()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var withHandler = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;

        var created = await raw.PostAsJsonAsync("/api/v1/rfqs", RfqBasics());
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
        return (raw, code);
    }

    private static async Task<string> CurrentETagAsync(HttpClient raw, string code)
    {
        var response = await raw.GetAsync($"/api/v1/rfqs/{code}");
        response.Headers.ETag.Should().NotBeNull();
        return response.Headers.ETag!.ToString();
    }

    [Fact]
    public async Task A_caller_without_the_permission_is_refused_before_the_body_is_judged()
    {
        // Holds real permissions, just not this one, so the refusal is a genuine authorization decision.
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await reviewer.PostAsJsonAsync(
            "/api/v1/rfqs/RFQ-2026-000001/requirements", BlankRequirement());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a caller who may not touch this route must not learn which of its fields are wrong");
    }

    [Fact]
    public async Task A_missing_precondition_is_refused_before_the_body_is_judged()
    {
        var (raw, code) = await OfficerWithDraftAsync();

        var response = await raw.SendAsync(
            Post($"/api/v1/rfqs/{code}/requirements", BlankRequirement(), ifMatch: null));

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired,
            "the precondition ran before validation when validation lived in the handler, and still does");
    }

    [Fact]
    public async Task With_the_permission_and_the_precondition_the_body_is_judged_and_named()
    {
        var (raw, code) = await OfficerWithDraftAsync();

        var response = await raw.SendAsync(
            Post($"/api/v1/rfqs/{code}/requirements", BlankRequirement(), await CurrentETagAsync(raw, code)));

        // The control for the two tests above: once nothing else stands in the way, the filter really does
        // run. Without this, both of them would also pass on a route that validated nothing at all.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");

        var fields = problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString())
            .ToList();
        fields.Should().Contain("textAr").And.Contain("textEn",
            "the field paths are what let the form mark the input the user got wrong");

        var first = problem.GetProperty("errors").EnumerateArray().First().GetProperty("messages");
        first.GetProperty("ar").GetString().Should().NotBeNullOrWhiteSpace(
            "both languages travel with the refusal, which is the whole reason this is not the framework's own");
        first.GetProperty("en").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_valid_body_reaches_the_handler()
    {
        var (raw, code) = await OfficerWithDraftAsync();

        var response = await raw.SendAsync(Post($"/api/v1/rfqs/{code}/requirements", new
        {
            textAr = "شرط",
            textEn = "A real requirement",
            isMandatory = true,
            documentTypeCode = (string?)null,
        }, await CurrentETagAsync(raw, code)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
