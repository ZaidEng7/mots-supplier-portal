using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// <c>GET /suppliers/{code}/documents/types/{typeCode}/history</c> — SCR-132.
///
/// <para>The gap it fills: a supplier could see the CURRENT version of a document and nothing else, so
/// "why was this rejected two versions ago" had no answer on any screen. The rule worth pinning is that
/// it authorizes the same way the single-document read does - the owner, or a reviewer holding
/// <c>supplier.document.review</c> - because a history that authorized differently would be the wider
/// of the two and nobody would notice which.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentHistoryTests(PostgresApiFixture fixture)
{
    private const string TypeCode = "tax_certificate";

    private static async Task UploadAsync(HttpClient client, string supplierCode)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(DocumentUploadTests.TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            // Required, not decoration: this type is ExpiryTracked, and omitting the date is a 400.
            { new StringContent("2027-03-15"), "expiryDate" },
        };
        var file = new ByteArrayContent(DocumentUploadTests.MinimalPdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "cert.pdf");

        var response = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private static async Task<List<JsonElement>> HistoryAsync(HttpClient client, string supplierCode)
    {
        var response = await client.GetAsync($"/api/v1/suppliers/{supplierCode}/documents/types/{TypeCode}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    [Fact]
    public async Task Returns_every_version_newest_first()
    {
        // Newest first: the current state is what a reader wants at the top, and the chain below it is
        // why it got there.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"History {Guid.NewGuid():N}"[..24]);
        var code = await client.OwnSupplierCodeAsync();

        await UploadAsync(client, code);
        await UploadAsync(client, code);
        await UploadAsync(client, code);

        var versions = await HistoryAsync(client, code);

        versions.Should().HaveCount(3);
        versions.Select(v => v.GetProperty("version").GetInt32()).Should().BeInDescendingOrder();
        versions[0].GetProperty("version").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_nothing_has_been_uploaded()
    {
        // A real answer, and a DIFFERENT one from "no such type": the supplier and the type both exist.
        // A 404 here would send a supplier looking for a document type that is on their own checklist.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistEmpty {Guid.NewGuid():N}"[..24]);

        var versions = await HistoryAsync(client, await client.OwnSupplierCodeAsync());

        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task Answers_404_for_a_document_type_that_does_not_exist()
    {
        // The control for the case above. An unknown TYPE is absent; an empty type is empty.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistNoType {Guid.NewGuid():N}"[..24]);
        var code = await client.OwnSupplierCodeAsync();

        var response = await client.GetAsync($"/api/v1/suppliers/{code}/documents/types/not_a_real_type/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reviewer_may_read_another_suppliers_history()
    {
        var owner = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistRev {Guid.NewGuid():N}"[..24]);
        var code = await owner.OwnSupplierCodeAsync();
        await UploadAsync(owner, code);

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        (await HistoryAsync(reviewer, code)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Another_supplier_gets_404_rather_than_403()
    {
        // §9.2: the row is absent, not forbidden - a 403 would confirm that this supplier code names a
        // real organisation, which is exactly what an opaque reference code is for.
        var owner = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistOwn {Guid.NewGuid():N}"[..24]);
        var code = await owner.OwnSupplierCodeAsync();
        await UploadAsync(owner, code);

        var stranger = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistOther {Guid.NewGuid():N}"[..24]);

        var response = await stranger.GetAsync($"/api/v1/suppliers/{code}/documents/types/{TypeCode}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Answers_404_for_a_supplier_that_does_not_exist()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"HistNoSup {Guid.NewGuid():N}"[..24]);

        var response = await client.GetAsync($"/api/v1/suppliers/SUP-999999/documents/types/{TypeCode}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
