// A real multipart upload through the actual endpoint, not a unit test against the handler in isolation.
//
// This is the class of test that would have caught the expiry date silently not binding: the parse used the
// current culture, which failed whenever the host's locale defaulted to a non-Gregorian calendar, even though the
// raw multipart field arrived correctly formatted.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentUploadTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Upload_with_expiry_date_persists_a_non_null_expiry_date()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Upload Test Co");

        using var content = new MultipartFormDataContent
        {
            { new StringContent(UploadFixtures.TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent("2027-03-15"), "expiryDate" },
        };
        var fileContent = new ByteArrayContent(UploadFixtures.MinimalPdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "cert.pdf");

        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("expiryDate").GetString().Should().Be("2027-03-15");
    }
}
