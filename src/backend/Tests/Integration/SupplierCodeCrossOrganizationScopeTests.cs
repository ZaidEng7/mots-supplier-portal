using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// RISK-004, for the five routes §12-A/C3 made code-addressed.
///
/// <para><b>This is the batch that gives up the structural property.</b> Until now the supplier
/// aggregate was protected by the shape of its own URLs: <c>/suppliers/me/documents</c> has no slot
/// for another supplier's identifier, so the attack could not be expressed and no check was needed.
/// §12.2 and §12.3 address suppliers by <c>{supplierCode}</c>, which hands every caller a way to
/// name someone else. What replaced the guarantee is one check
/// (<c>ISupplierCodeScope</c>) and these tests.</para>
///
/// <para><b>404 with an identical body, not 403.</b> §9.2: *"Out-of-scope access to an existing
/// resource returns 404 (not 403) to avoid leaking existence."* Each case asserts the status AND
/// that the body is byte-identical to the same call against a supplier code that never existed.</para>
///
/// <para><b>Every case has an owner control.</b> A negative that passes because the route does not
/// exist looks exactly like one that passes because scoping works - which this project has already
/// been bitten by once, in §12-A/C2.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierCodeCrossOrganizationScopeTests(PostgresApiFixture fixture)
{
    private const string NeverExistedSupplierCode = "SUP-2026-999999";

    private async Task<(HttpClient Client, string Code, Guid Id)> SupplierAsync(string label)
    {
        var name = $"{label} {Guid.NewGuid():N}"[..26];
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        return (client, row.ReferenceCode, row.Id);
    }

    private static async Task AssertIndistinguishableAsync(
        Func<string, Task<HttpResponseMessage>> attempt, string othersCode)
    {
        var outOfScope = await attempt(othersCode);
        var neverExisted = await attempt(NeverExistedSupplierCode);

        outOfScope.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out-of-scope access to an existing resource returns 404, not 403");
        neverExisted.StatusCode.Should().Be(outOfScope.StatusCode);
        (await outOfScope.Content.ReadAsStringAsync())
            .Should().Be(await neverExisted.Content.ReadAsStringAsync(),
                "otherwise the response itself tells the caller that the other supplier exists");
    }

    // ---- §12.2 PATCH /suppliers/{supplierCode} -------------------------------------------------

    [Fact]
    public async Task A_supplier_cannot_patch_another_suppliers_profile()
    {
        var (a, _, _) = await SupplierAsync("PatchA");
        var (_, bCode, _) = await SupplierAsync("PatchB");

        await AssertIndistinguishableAsync(
            code => a.PatchAsJsonAsync($"/api/v1/suppliers/{code}",
                new { description = "hijacked", website = (string?)null, supplierGroup = (string?)null, currencyCode = (string?)null, primaryContactPhone = (string?)null }),
            bCode);
    }

    [Fact]
    public async Task The_owner_can_patch_their_own_profile()
    {
        var (a, aCode, _) = await SupplierAsync("PatchOwner");

        var response = await a.PatchAsJsonAsync($"/api/v1/suppliers/{aCode}",
            new { description = "mine", website = (string?)null, supplierGroup = (string?)null, currencyCode = (string?)null, primaryContactPhone = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "control: the negative above must fail because of scoping, not because the route is broken");
    }

    // ---- §12.2 POST /suppliers/{supplierCode}/onboarding/submit -------------------------------

    [Fact]
    public async Task A_supplier_cannot_submit_another_suppliers_application()
    {
        var (a, _, _) = await SupplierAsync("SubmitA");
        var (_, bCode, _) = await SupplierAsync("SubmitB");

        await AssertIndistinguishableAsync(
            code => a.PostAsync($"/api/v1/suppliers/{code}/onboarding/submit", null), bCode);
    }

    /// <summary>
    /// The owner control here asserts NOT-404 rather than 200: a fresh supplier's profile is
    /// incomplete, so the honest answer is 422 with the missing fields (§12.2). What matters is that
    /// the request REACHED the handler instead of being turned away by the scope check.
    /// </summary>
    [Fact]
    public async Task The_owner_reaches_their_own_submit_endpoint()
    {
        var (a, aCode, _) = await SupplierAsync("SubmitOwner");

        var response = await a.PostAsync($"/api/v1/suppliers/{aCode}/onboarding/submit", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "control: the owner's own code must pass the scope check and reach the handler");
    }

    // ---- §12.3 GET/POST /suppliers/{supplierCode}/documents -----------------------------------

    [Fact]
    public async Task A_supplier_cannot_list_another_suppliers_documents()
    {
        var (a, _, _) = await SupplierAsync("DocListA");
        var (_, bCode, _) = await SupplierAsync("DocListB");

        await AssertIndistinguishableAsync(
            code => a.GetAsync($"/api/v1/suppliers/{code}/documents"), bCode);
    }

    [Fact]
    public async Task The_owner_can_list_their_own_documents()
    {
        var (a, aCode, _) = await SupplierAsync("DocListOwner");

        (await a.GetAsync($"/api/v1/suppliers/{aCode}/documents")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control");
    }

    [Fact]
    public async Task A_supplier_cannot_upload_into_another_suppliers_documents()
    {
        var (a, _, _) = await SupplierAsync("DocUpA");
        var (_, bCode, _) = await SupplierAsync("DocUpB");

        await AssertIndistinguishableAsync(
            code =>
            {
                var content = new MultipartFormDataContent();
                var file = new ByteArrayContent("%PDF-1.4 test"u8.ToArray());
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                content.Add(file, "file", "planted.pdf");
                content.Add(new StringContent("commercial_registration"), "documentTypeCode");
                return a.PostAsync($"/api/v1/suppliers/{code}/documents", content);
            },
            bCode);
    }

    /// <summary>
    /// The scope check runs BEFORE the multipart body is read, so an out-of-scope caller cannot even
    /// stream a file. Asserted by sending a request with no body at all: it must still be the
    /// scope's 404, never the 400 a malformed upload would earn.
    /// </summary>
    [Fact]
    public async Task An_out_of_scope_upload_is_refused_before_the_body_is_examined()
    {
        var (a, _, _) = await SupplierAsync("DocUpEarlyA");
        var (_, bCode, _) = await SupplierAsync("DocUpEarlyB");

        var response = await a.PostAsync($"/api/v1/suppliers/{bCode}/documents", new StringContent(""));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a 400 here would mean the request got as far as being parsed, which tells the caller " +
            "their code was accepted as in-scope");
    }

    // ---- §3 POST /suppliers/{supplierCode}/documents/{documentId}/approve|reject ---------------

    /// <summary>
    /// Reviewer-facing, so the negative is different in kind: the reviewer legitimately acts across
    /// suppliers, and what must be refused is a document reached under the WRONG supplier's code.
    /// Without that check a reviewer could approve B's document through A's URL, and the audit row
    /// would name the wrong supplier.
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_reviewer_cannot_transition_a_document_under_the_wrong_suppliers_code(string transition)
    {
        var (a, aCode, aId) = await SupplierAsync("RevDocA");
        var (_, bCode, _) = await SupplierAsync("RevDocB");
        var documentId = await SeedDocumentAsync(aId);

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        var body = new { reason = "wrong owner" };
        var wrongOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{bCode}/documents/{documentId}/{transition}", body);
        var unknownOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{NeverExistedSupplierCode}/documents/{documentId}/{transition}", body);

        wrongOwner.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the document exists, but not under B - and answering anything else confirms it exists");
        (await wrongOwner.Content.ReadAsStringAsync()).Should().Be(await unknownOwner.Content.ReadAsStringAsync());

        // Control: the same reviewer, the same document, under its REAL owner's code, is not
        // refused by the scope check.
        var rightOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{aCode}/documents/{documentId}/{transition}", body);
        rightOwner.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "control: under the correct supplier code the request must reach the handler");
        _ = a;
    }

    private async Task<Guid> SeedDocumentAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

        var document = SupplierDocument.CreatePendingScan(
            supplierId, type.Id, version: 1, quarantineKey: $"seed/{Guid.NewGuid():N}",
            originalFileName: "seed.pdf", contentType: "application/pdf", sizeBytes: 1024,
            uploadedByUserId: Guid.CreateVersion7(), issueDate: null, expiryDate: null,
            expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow));
        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }
}
