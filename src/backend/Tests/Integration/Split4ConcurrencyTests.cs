using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-030 split (4): the logo, terms acceptance, and the two document decisions.
///
/// <para>Raw clients throughout, for the reason SupplierChildWriteConcurrencyTests states: the fixture's
/// usual client attaches a fresh If-Match before every mutation, which would make every assertion here
/// vacuous.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class Split4ConcurrencyTests(PostgresApiFixture fixture)
{
    private async Task<HttpClient> RawSupplierAsync(string name)
    {
        var withHandler = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);
        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = withHandler.DefaultRequestHeaders.Authorization;
        return raw;
    }

    private static async Task<string> SupplierETagAsync(HttpClient raw)
    {
        var response = await raw.GetAsync("/api/v1/suppliers/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull("GET /suppliers/me must issue the precondition these writes require");
        return response.Headers.ETag!.ToString();
    }

    private static HttpRequestMessage Post(string path, string? ifMatch, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    [Fact]
    public async Task Accepting_terms_requires_the_version_the_caller_read()
    {
        var raw = await RawSupplierAsync($"Terms{Guid.NewGuid():N}"[..12]);

        // Without the header: 428, not 409. §8.1 - a missing precondition is a different failure from a
        // stale one, and answering 409 would tell the caller to reconcile something.
        (await raw.SendAsync(Post("/api/v1/suppliers/me/accept-terms", ifMatch: null)))
            .StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        var etag = await SupplierETagAsync(raw);
        var accepted = await raw.SendAsync(Post("/api/v1/suppliers/me/accept-terms", etag));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        // WithFreshETag: the write returns the version it produced, so a caller can make a second guarded
        // write without a re-read. Without it a supplier's second edit 428s - the split (3) defect.
        accepted.Headers.ETag.Should().NotBeNull("the write must hand back the version it produced");

        // And the version it produced is NOT the one that was sent, which is what makes the guard mean
        // something: replaying the old precondition is now a stale write.
        var replay = await raw.SendAsync(Post("/api/v1/suppliers/me/accept-terms", etag));
        replay.StatusCode.Should().BeOneOf(HttpStatusCode.PreconditionFailed, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Uploading_a_logo_requires_the_version_too
        ()
    {
        var raw = await RawSupplierAsync($"Logo{Guid.NewGuid():N}"[..12]);

        // A one-pixel PNG, magic bytes and all: FileTypeSniffer checks the bytes against the declared type,
        // so a text file called .png would be refused for the wrong reason and prove nothing about If-Match.
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        ];

        static MultipartFormDataContent Form(byte[] bytes)
        {
            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "logo.png");
            return content;
        }

        (await raw.SendAsync(Post("/api/v1/suppliers/me/logo", ifMatch: null, Form(png))))
            .StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        var etag = await SupplierETagAsync(raw);
        var uploaded = await raw.SendAsync(Post("/api/v1/suppliers/me/logo", etag, Form(png)));
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        uploaded.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task Uploading_a_DOCUMENT_deliberately_does_not_require_a_version()
    {
        // The exclusion, asserted so it is a decision rather than a gap someone closes by accident. An upload
        // ADDS a row and cannot overwrite another upload, and this route returns the document rather than the
        // supplier - so it has no fresh version to hand back, and guarding it would 428 a supplier's second
        // upload with nothing on screen to explain it.
        var raw = await RawSupplierAsync($"Doc{Guid.NewGuid():N}"[..12]);
        var me = await raw.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var supplierCode = me.GetProperty("supplierCode").GetString();

        // Reusing DocumentUploadTests' own fixture constants rather than reading the reference list: the
        // form takes documentTypeId and expiryDate (checked against that suite, not guessed - my first
        // version sent documentTypeCode and expiresOn to a reference route that does not exist).
        var content = new MultipartFormDataContent
        {
            { new StringContent(DocumentUploadTests.TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent("2027-03-15"), "expiryDate" },
        };
        var file = new ByteArrayContent(DocumentUploadTests.MinimalPdfBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "cert.pdf");

        var response = await raw.SendAsync(Post($"/api/v1/suppliers/{supplierCode}/documents", ifMatch: null, content));
        response.StatusCode.Should().NotBe(HttpStatusCode.PreconditionRequired,
            "an upload is an addition, not an overwrite - see the endpoint's own note");
    }

    [Fact]
    public async Task A_document_decision_requires_the_version_and_the_reviewers_read_issues_it()
    {
        // The pair that matters: two reviewers deciding the same document is one decision silently replacing
        // the other, and this is the only route in split (4) where that can happen.
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Rev{Guid.NewGuid():N}"[..12]);
        var me = await supplier.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var supplierCode = me.GetProperty("supplierCode").GetString();

        var reviewerWithHandler = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var reviewer = fixture.CreateRawClient();
        reviewer.DefaultRequestHeaders.Authorization = reviewerWithHandler.DefaultRequestHeaders.Authorization;

        // The read half, added in the same change as the guard: without an ETag here the reviewer could not
        // obtain the precondition their own decision requires, which is batch 3's Offering mistake.
        var view = await reviewer.GetAsync($"/api/v1/review/{supplierCode}");
        view.StatusCode.Should().Be(HttpStatusCode.OK, await view.Content.ReadAsStringAsync());
        view.Headers.ETag.Should().NotBeNull(
            "a guarded decision needs a read that issues its precondition - GET /review/{code} is that read");

        // And the decision itself refuses a caller who did not read.
        var refused = await reviewer.SendAsync(
            Post($"/api/v1/suppliers/{supplierCode}/documents/does-not-matter/approve", ifMatch: null));
        refused.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired,
            "the precondition is checked before the document is looked up, which is what makes it a guard");
    }
}
