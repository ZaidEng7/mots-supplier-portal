using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A real multipart upload through the actual endpoint, not a unit test against the handler in
/// isolation - this is the class of test that would have caught the expiryDate-not-binding bug
/// (MSP-60): DateOnly.TryParse(form["expiryDate"]) using CurrentCulture silently failed whenever
/// the host's OS/container locale defaulted to a non-Gregorian calendar (e.g. en-SA), even though
/// the raw multipart field arrived correctly formatted.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentUploadTests(PostgresApiFixture fixture)
{
    private static readonly Guid TaxCertificateDocumentTypeId = Guid.Parse("00000000-0000-0000-0000-000000000102");

    private static readonly byte[] MinimalPdfBytes =
        "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"u8.ToArray();

    private async Task<HttpClient> CreateAuthenticatedSupplierClientAsync()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";
        const string password = "IntegrationTest#2026!";

        await client.PostAsJsonAsync("/api/v1/registrations", new
        {
            displayNameAr = "شركة اختبار الرفع",
            displayNameEn = "Integration Upload Test Co",
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Upload Tester",
            representativePhone = "+963900000099",
            email,
            password,
        });

        using var scope = fixture.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var securityTokenService = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();
        var user = await userManager.FindByEmailAsync(email);
        var rawToken = await securityTokenService.IssueAsync(user!.Id, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), CancellationToken.None);
        await client.PostAsJsonAsync("/api/v1/registrations/verify", new { token = rawToken });

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    [Fact]
    public async Task Upload_with_expiry_date_persists_a_non_null_expiry_date()
    {
        var client = await CreateAuthenticatedSupplierClientAsync();

        using var content = new MultipartFormDataContent
        {
            { new StringContent(TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent("2027-03-15"), "expiryDate" },
        };
        var fileContent = new ByteArrayContent(MinimalPdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "cert.pdf");

        var response = await client.PostAsync("/api/v1/suppliers/me/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("expiryDate").GetString().Should().Be("2027-03-15");
    }
}
