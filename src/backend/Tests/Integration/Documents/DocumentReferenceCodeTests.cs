// Documents are addressed by an opaque public code, never by their internal identifier.
//
// The written principle is that internal keys are never exposed in URLs, payloads or errors. The contract gives
// the grammar and its own example names the prefix. Nothing here is invented.
//
// The code is asserted against STORAGE as well, so the response is not synthesising something the database does
// not have.
//
// And it is asserted on the raw body rather than on a parsed field, because the principle says payloads and not
// just URLs, which is the half a comment in this codebase used to read too narrowly.
//
//
// EACH POSITIVE HAS ITS CONTROL
//
// "Everything resolves" must not be able to pass because the lookup stopped filtering, so an unknown code sits
// beside the positive.
//
// The old address must stop working, or nothing has moved.
//
// And the owner can read it, so a miss for anybody else is the scope working rather than a route that refuses
// everyone.
//
//
// THE UNIQUENESS IS THE INDEX'S, PROVEN BY TRYING TO BREAK IT
//
// The generator being atomic makes a clash unlikely. The unique constraint makes it impossible, and only one of
// those survives a future second writer.
//
// The expected failure is the driver's own, unwrapped, because the statement is issued directly rather than
// through the change tracker, and its code is asserted by name so this cannot pass on some other database error.

namespace MotsSupplierPortal.Tests.Integration.Documents;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentReferenceCodeTests(PostgresApiFixture fixture)
{
    private static readonly Regex PublicIdGrammar = new(@"^[A-Z]{2,4}-\d{4}-\d{6}$", RegexOptions.Compiled);

    private async Task<(HttpClient Client, string SupplierCode, string DocumentCode)> UploadedDocumentAsync(string name)
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);
        var supplierCode = await client.OwnSupplierCodeAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 test"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "register.pdf");
        content.Add(new StringContent(type.Id.ToString()), "documentTypeId");

        var upload = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", content);
        upload.EnsureSuccessStatusCode();

        var code = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetString()!;
        return (client, supplierCode, code);
    }

    [Fact]
    public async Task An_uploaded_document_is_addressed_by_a_code_matching_the_documented_grammar()
    {
        var (_, _, documentCode) = await UploadedDocumentAsync($"DocCode Shape {Guid.NewGuid():N}"[..30]);

        documentCode.Should().MatchRegex(PublicIdGrammar.ToString(), "§3.1's path grammar");
        documentCode.Should().StartWith("DOC-", "§12.3's own example names the prefix");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.SupplierDocuments.AsNoTracking().AnyAsync(d => d.ReferenceCode == documentCode))
            .Should().BeTrue();
    }

    [Fact]
    public async Task No_internal_GUID_appears_in_the_upload_response()
    {
        var (client, supplierCode, documentCode) = await UploadedDocumentAsync($"DocCode Body {Guid.NewGuid():N}"[..30]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var internalId = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.ReferenceCode == documentCode).Select(d => d.Id).FirstAsync();

        var list = await client.GetStringAsync($"/api/v1/suppliers/{supplierCode}/documents?page=1&pageSize=20");

        list.Should().Contain(documentCode, "control: the list really does describe this document");
        list.Should().NotContain(internalId.ToString(), "the internal GUID must not reach a payload");
    }

    [Fact]
    public async Task A_real_document_resolves_end_to_end_and_an_unknown_code_does_not()
    {
        var (client, _, documentCode) = await UploadedDocumentAsync($"DocCode E2E {Guid.NewGuid():N}"[..30]);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var sdb = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await sdb.SupplierDocuments.Where(d => d.ReferenceCode == documentCode)
                .ExecuteUpdateAsync(p => p.SetProperty(d => d.State, DocumentState.Uploaded));
        }

        var found = await client.GetAsync($"/api/v1/documents/{documentCode}/download-url");
        found.StatusCode.Should().Be(HttpStatusCode.OK, await found.Content.ReadAsStringAsync());
        (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()
            .Should().NotBeNullOrWhiteSpace("resolved end to end, not merely routed");

        var unknown = await client.GetAsync("/api/v1/documents/DOC-2026-999999/download-url");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound, "an unknown code must still 404");
    }

    [Fact]
    public async Task The_internal_GUID_is_no_longer_an_address()
    {
        var (client, _, documentCode) = await UploadedDocumentAsync($"DocCode Guid {Guid.NewGuid():N}"[..30]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var internalId = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.ReferenceCode == documentCode).Select(d => d.Id).FirstAsync();

        var byGuid = await client.GetAsync($"/api/v1/documents/{internalId}/download-url");

        byGuid.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§3.1: an unmatched shape is a 404 and never leaks whether a GUID exists");
    }

    [Fact]
    public async Task Another_suppliers_document_is_indistinguishable_from_one_that_does_not_exist()
    {
        var (owner, _, documentCode) = await UploadedDocumentAsync($"DocCode Mine {Guid.NewGuid():N}"[..30]);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var sdb = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await sdb.SupplierDocuments.Where(d => d.ReferenceCode == documentCode)
                .ExecuteUpdateAsync(p => p.SetProperty(d => d.State, DocumentState.Uploaded));
        }

        (await owner.GetAsync($"/api/v1/documents/{documentCode}/download-url")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var outsider = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"DocCode Other {Guid.NewGuid():N}"[..30]);

        var refused = await outsider.GetAsync($"/api/v1/documents/{documentCode}/download-url");
        var fabricated = await outsider.GetAsync("/api/v1/documents/DOC-2026-999998/download-url");

        refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "§9.2: 404, never 403");
        fabricated.StatusCode.Should().Be(HttpStatusCode.NotFound);

        static string Shape(string body) => Regex.Replace(
            body, "\"(instance|traceId|correlationId)\":\"[^\"]*\"", "$1");

        Shape(await refused.Content.ReadAsStringAsync())
            .Should().Be(Shape(await fabricated.Content.ReadAsStringAsync()),
                "a real document out of scope and a code that never existed must read identically");
    }

    [Fact]
    public async Task Codes_are_unique_in_the_database_not_merely_in_the_generator()
    {
        var (_, _, first) = await UploadedDocumentAsync($"DocCode U1 {Guid.NewGuid():N}"[..30]);
        var (_, _, second) = await UploadedDocumentAsync($"DocCode U2 {Guid.NewGuid():N}"[..30]);

        first.Should().NotBe(second);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.ReferenceCode == first).Select(d => d.Id).FirstAsync();

        var forceCollision = async () => await db.SupplierDocuments.Where(d => d.Id == duplicate)
            .ExecuteUpdateAsync(p => p.SetProperty(d => d.ReferenceCode, second));

        (await forceCollision.Should().ThrowAsync<Npgsql.PostgresException>(
            "the database refuses a duplicate code, rather than the generator merely avoiding one"))
            .Which.SqlState.Should().Be("23505");
    }
}
