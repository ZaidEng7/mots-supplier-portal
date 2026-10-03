// Five handlers that wrote an audit row which never reached the database, each proved stored by the real endpoint.
//
// The audit logger only adds a row to the caller's unit of work, and the caller's save is what stores it. Two of
// these handlers had no save at all: revealing a bank account number, and refusing an upload whose bytes did not
// match its declared type. Three added their row after their last save: disabling a colleague, registering, and
// verifying an email address. Every one answered success and left no trace, and a decrypted bank account number
// shown with no stored record of who saw it is the case that mattered most.
//
// Each test drives the real endpoint, then reads the row back through a fresh scope, so a row still sitting in the
// request's own unit of work cannot pass. Each checks the action, the record it is filed under and the person who
// did it, because a row stored under the wrong supplier or with no actor answers none of the questions an audit
// trail is read for. The action names are the stored values the trail is searched by, and are spelled out here
// rather than shared with the handlers, so renaming one fails a test.
//
// Registering and verifying are driven by the shared helper that makes every supplier, through the same two
// endpoints a person uses, so those two tests read the rows that helper's requests left.
//
// The reveal test also checks that the row holds only the masked number. The plain one is what was revealed, and
// writing it into the trail would turn the record of an access into a second copy of the secret.
//
// The refused upload is saved on its own path, so its test also checks the save carried nothing else. An earlier
// version of the same document is uploaded first and must still be the latest afterwards, with no new version
// beside it: a refused upload that superseded the version it failed to replace would leave the supplier with no
// current certificate.
//
// Every test makes its own supplier through the shared helper, so nothing here touches a row another test reads.
// Audit rows cannot be deleted by design, which is why each is found by the supplier it belongs to rather than by
// counting a table every other test also writes to.

namespace MotsSupplierPortal.Tests.Integration.Audit;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuditRowPersistenceTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid UserId, Guid SupplierId)> SupplierAdminAsync(string displayNameEn)
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, displayNameEn);

        await using var scope = fixture.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(email);

        return (client, user!.Id, user.SupplierId!.Value);
    }

    private async Task<AuditLog> SingleRowAsync(Guid aggregateId, string action)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == aggregateId && a.Action == action)
            .ToListAsync();

        rows.Should().ContainSingle($"one {action} row must be stored, not only added to a unit of work nobody saved");
        return rows[0];
    }

    private static MultipartFormDataContent TaxCertificateUpload(byte[] bytes)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(UploadFixtures.TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "expiryDate" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "cert.pdf");
        return content;
    }

    [Fact]
    public async Task Disabling_a_colleague_stores_its_audit_row()
    {
        var (client, adminId, supplierId) = await SupplierAdminAsync("Disable Audit Co");
        await SupplierTestClient.CreateColleagueAsync(fixture, supplierId);

        Guid colleagueId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            colleagueId = await db.Users.Where(u => u.SupplierId == supplierId && u.Id != adminId)
                .Select(u => u.Id).SingleAsync();
        }

        var response = await client.PostAsync($"/api/v1/suppliers/me/users/{colleagueId}/disable", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var row = await SingleRowAsync(supplierId, "supplier_user_disabled");
        row.AggregateType.Should().Be("Supplier");
        row.ActorUserId.Should().Be(adminId, "the administrator who disabled the account is the accountable actor");
        row.ActorKind.Should().Be(AuditActorKind.User);
    }

    [Fact]
    public async Task Revealing_a_bank_account_number_stores_its_audit_row_with_only_the_masked_number()
    {
        const string AccountNumber = "SY0000000000000000004242";
        var (client, adminId, supplierId) = await SupplierAdminAsync("Reveal Audit Co");

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/bank-accounts", new
        {
            accountHolderName = "Reveal Holder",
            bankName = "Reveal Bank",
            branchName = "Main",
            accountNumber = AccountNumber,
            swiftBic = (string?)null,
            currencyCode = "SYP",
        })).EnsureSuccessStatusCode();

        Guid bankAccountId;
        string masked;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.BankAccounts.AsNoTracking().SingleAsync(b => b.SupplierId == supplierId);
            bankAccountId = account.Id;
            masked = account.MaskedAccountNumber;
        }

        var response = await client.PostAsync($"/api/v1/suppliers/me/bank-accounts/{bankAccountId}/reveal", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountNumber").GetString()
            .Should().Be(AccountNumber, "control: the endpoint really revealed the number this row records");

        var row = await SingleRowAsync(supplierId, "bank_account_revealed");
        row.AggregateType.Should().Be("Supplier");
        row.ActorUserId.Should().Be(adminId, "who saw the number is the whole point of the row");
        row.ActorKind.Should().Be(AuditActorKind.User);
        row.Reason.Should().Be(masked);
        new[] { row.Reason, row.Changes, row.ActorLabel, row.ReferenceCode }
            .Should().NotContain(v => v != null && v.Contains(AccountNumber),
                "the trail records that the number was seen, never the number itself");
    }

    [Fact]
    public async Task Refusing_an_upload_whose_content_does_not_match_its_type_stores_its_audit_row_and_nothing_else()
    {
        var (client, adminId, supplierId) = await SupplierAdminAsync("Mismatch Audit Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        using (var genuine = TaxCertificateUpload(UploadFixtures.MinimalPdfBytes))
        {
            var first = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", genuine);
            first.StatusCode.Should().Be(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        }

        using var disguised = TaxCertificateUpload("this is plain text claiming to be a PDF"u8.ToArray());
        var response = await client.PostAsync($"/api/v1/suppliers/{supplierCode}/documents", disguised);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("CONTENT_TYPE_MISMATCH", "control: the refusal under test is the content mismatch");

        var row = await SingleRowAsync(supplierId, "document_upload_content_mismatch");
        row.AggregateType.Should().Be("SupplierDocument");
        row.ActorUserId.Should().Be(adminId);
        row.ActorKind.Should().Be(AuditActorKind.User);
        row.ReferenceCode.Should().Be(supplierCode);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var versions = await db.SupplierDocuments.AsNoTracking()
            .Where(d => d.SupplierId == supplierId)
            .Select(d => new { d.Version, d.IsLatestVersion })
            .ToListAsync();
        versions.Should().ContainSingle("the refused upload must not have stored a version of its own")
            .Which.Should().Be(new { Version = 1, IsLatestVersion = true },
                "the save that stores the refusal's audit row must not carry a supersede of the version it failed to replace");
    }

    [Fact]
    public async Task Registering_stores_its_audit_row()
    {
        var (client, userId, supplierId) = await SupplierAdminAsync("Register Audit Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        var row = await SingleRowAsync(supplierId, "register");
        row.AggregateType.Should().Be("Supplier");
        row.ActorUserId.Should().Be(userId, "the account created by the registration is the one that registered");
        row.ActorKind.Should().Be(AuditActorKind.User);
        row.ActorLabel.Should().Be("Integration Tester");
        row.ToState.Should().Be("Draft");
        row.ReferenceCode.Should().Be(supplierCode);
    }

    [Fact]
    public async Task Verifying_an_email_address_stores_its_state_change_row()
    {
        var (client, userId, supplierId) = await SupplierAdminAsync("Verify Audit Co");
        var supplierCode = await client.OwnSupplierCodeAsync();

        var row = await SingleRowAsync(supplierId, "state_change");
        row.AggregateType.Should().Be("Supplier");
        row.ActorUserId.Should().Be(userId);
        row.ActorKind.Should().Be(AuditActorKind.User);
        row.FromState.Should().Be("Draft");
        row.ToState.Should().Be("EmailVerified");
        row.ReferenceCode.Should().Be(supplierCode);
    }
}
