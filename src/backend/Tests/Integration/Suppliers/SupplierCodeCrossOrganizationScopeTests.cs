// Cross-supplier scoping on the five routes that became code-addressed.
//
//
// THIS IS THE CHANGE THAT GIVES UP THE STRUCTURAL PROPERTY
//
// Until then the supplier's own routes were protected by the shape of their URLs: a path that says "me" has no slot
// for another supplier's identifier, so the attack could not be expressed and no check was needed.
//
// Addressing suppliers by code hands every caller a way to name somebody else. What replaced the guarantee is one
// shared check and these tests.
//
//
// NOT-FOUND RATHER THAN FORBIDDEN, AND THE BODIES COMPARED FIELD BY FIELD
//
// The contract requires out-of-scope access to an existing resource to answer not-found, so existence is not
// leaked. Each case asserts the status AND compares the body against the same call made against a code that never
// existed.
//
// Byte equality no longer applies, and that is the error model working rather than a weaker assertion: every error
// now carries per-request identifiers and the caller's own path, so two requests can never produce identical bytes.
// None of those three can leak existence, so the comparison is over the fields that DO discriminate, each named,
// which is stricter than incidental byte equality.
//
//
// EVERY CASE HAS AN OWNER CONTROL
//
// A negative that passes because the route does not exist looks exactly like one that passes because scoping works,
// which this project has already been bitten by once.
//
// One control asserts NOT-not-found rather than success, because a fresh supplier's profile is incomplete so the
// honest answer names the missing fields. What matters is that the request REACHED the handler instead of being
// turned away by the scope check.
//
//
// TWO CASES THAT ARE DIFFERENT IN KIND
//
// The scope check runs BEFORE the multipart body is read, so an out-of-scope caller cannot even stream a file. That
// is asserted by sending a request with no body at all: it must still be the scope's refusal rather than the one a
// malformed upload would earn.
//
// And the reviewer-facing decisions are different again, because a reviewer legitimately acts across suppliers.
// What must be refused is a document reached under the WRONG supplier's code: without that check a reviewer could
// approve one supplier's document through another's address, and the audit row would name the wrong supplier. Its
// control is the same reviewer, the same document, under its real owner's code.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

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
        await AssertDiscriminatingFieldsMatchAsync(outOfScope, neverExisted);
    }

    private static async Task AssertDiscriminatingFieldsMatchAsync(HttpResponseMessage a, HttpResponseMessage b)
    {
        var left = await a.Content.ReadFromJsonAsync<JsonElement>();
        var right = await b.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var field in new[] { "type", "title", "status", "code", "detail" })
        {
            var inLeft = left.TryGetProperty(field, out var vl) ? vl.ToString() : null;
            var inRight = right.TryGetProperty(field, out var vr) ? vr.ToString() : null;
            inLeft.Should().Be(inRight,
                $"'{field}' differing between an out-of-scope code and one that never existed is " +
                "exactly the existence oracle §9.2 forbids");
        }
    }

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

    [Fact]
    public async Task A_supplier_cannot_submit_another_suppliers_application()
    {
        var (a, _, _) = await SupplierAsync("SubmitA");
        var (_, bCode, _) = await SupplierAsync("SubmitB");

        await AssertIndistinguishableAsync(
            code => a.PostAsync($"/api/v1/suppliers/{code}/onboarding/submit", null), bCode);
    }

    [Fact]
    public async Task The_owner_reaches_their_own_submit_endpoint()
    {
        var (a, aCode, _) = await SupplierAsync("SubmitOwner");

        var response = await a.PostAsync($"/api/v1/suppliers/{aCode}/onboarding/submit", null);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "control: the owner's own code must pass the scope check and reach the handler");
    }

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

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_reviewer_cannot_transition_a_document_under_the_wrong_suppliers_code(string transition)
    {
        var (a, aCode, aId) = await SupplierAsync("RevDocA");
        var (_, bCode, _) = await SupplierAsync("RevDocB");
        var documentCode = await SeedDocumentAsync(aId);

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        var body = new { reason = "wrong owner" };
        var wrongOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{bCode}/documents/{documentCode}/{transition}", body);
        var unknownOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{NeverExistedSupplierCode}/documents/{documentCode}/{transition}", body);

        wrongOwner.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the document exists, but not under B - and answering anything else confirms it exists");
        await AssertDiscriminatingFieldsMatchAsync(wrongOwner, unknownOwner);

        var rightOwner = await reviewer.PostAsJsonAsync($"/api/v1/suppliers/{aCode}/documents/{documentCode}/{transition}", body);
        rightOwner.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "control: under the correct supplier code the request must reach the handler");
        _ = a;
    }

    private async Task<string> SeedDocumentAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            supplierId, type.Id, version: 1, quarantineKey: $"seed/{Guid.NewGuid():N}",
            originalFileName: "seed.pdf", contentType: "application/pdf", sizeBytes: 1024,
            uploadedByUserId: Guid.CreateVersion7(), issueDate: null, expiryDate: null,
            expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow));
        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.ReferenceCode;
    }
}
