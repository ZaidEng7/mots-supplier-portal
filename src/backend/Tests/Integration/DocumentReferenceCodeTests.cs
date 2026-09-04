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

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-010: documents are addressed by an opaque public code, never by their internal GUID.
///
/// <para>API-ARCHITECTURE.md §3 principle 3: <i>"internal GUIDv7 / integer PKs are never exposed in
/// URLs, payloads, or errors"</i>. §3.1 gives the grammar - <c>^[A-Z]{2,4}-\d{4}-\d{6}$</c> - and
/// §12.3's own example names the prefix, <c>DOC-2026-013377</c>. Nothing here is invented.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentReferenceCodeTests(PostgresApiFixture fixture)
{
    /// <summary>§3.1's path grammar, verbatim.</summary>
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

        // Asserted against STORAGE: the row really carries this code, so the response is not
        // synthesising something the database does not have.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.SupplierDocuments.AsNoTracking().AnyAsync(d => d.ReferenceCode == documentCode))
            .Should().BeTrue();
    }

    [Fact]
    public async Task No_internal_GUID_appears_in_the_upload_response()
    {
        // §3 says payloads, not just URLs - the half a comment in this codebase used to read too
        // narrowly, and the reason it is asserted on the raw body rather than on a parsed field.
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
        // "Everything resolves" must not be able to pass because the lookup stopped filtering, so
        // the unknown-code control sits beside the positive.
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
        // The migration's whole point: the old address must stop working, or nothing has moved.
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

        // Owner control: the supplier who uploaded it can read it, so the 404 below is the scope
        // working rather than a route that refuses everyone.
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

        // The INDEX, not the generator, is what makes a collision impossible - asserted by trying
        // to create one. The generator being atomic (MSP-81) makes a clash unlikely; the unique
        // constraint makes it impossible, and only one of those survives a future second writer.
        var forceCollision = async () => await db.SupplierDocuments.Where(d => d.Id == duplicate)
            .ExecuteUpdateAsync(p => p.SetProperty(d => d.ReferenceCode, second));

        // PostgresException, not DbUpdateException: ExecuteUpdateAsync issues SQL directly rather
        // than going through the change tracker, so the driver's error surfaces unwrapped. 23505 is
        // unique_violation, asserted by name so this cannot pass on some other database error.
        (await forceCollision.Should().ThrowAsync<Npgsql.PostgresException>(
            "the database refuses a duplicate code, rather than the generator merely avoiding one"))
            .Which.SqlState.Should().Be("23505");
    }
}
