using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-64. Every audit call site used to pass its own <c>Guid.NewGuid()</c> as the correlation id -
/// 51 of them - so the column was populated, indexed, and correlated to nothing.
///
/// A populated column is not evidence of a fix here; moving the <c>Guid.NewGuid()</c> into the
/// logger would also produce populated, unique values. These tests check the two properties that
/// distinguish a real correlation id: that it is SHARED across rows from one request, and that it
/// is the REQUEST'S TRACE ID rather than an unrelated value that merely looks like one.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuditCorrelationTests(PostgresApiFixture fixture)
{
    /// <summary>Builds a W3C traceparent with a known trace id, so the test knows in advance what
    /// the audit row must contain. ASP.NET Core adopts an inbound traceparent as the current
    /// Activity, which is what makes this assertable from outside the process.</summary>
    private static (string Header, Guid ExpectedCorrelationId) TraceParent()
    {
        var traceId = Guid.NewGuid();
        var hex = Convert.ToHexString(traceId.ToByteArray()).ToLowerInvariant();
        return ($"00-{hex}-{Convert.ToHexString(Random.Shared.NextInt64().ToString("x16").Select(c => (byte)c).ToArray())[..16].ToLowerInvariant()}-01", traceId);
    }

    [Fact]
    public async Task Audit_correlation_id_is_the_trace_id_of_the_request_that_produced_it()
    {
        var (header, expected) = TraceParent();
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Trace Co");
        client.DefaultRequestHeaders.Remove("traceparent");
        client.DefaultRequestHeaders.Add("traceparent", header);

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/contacts", new
        {
            fullName = "Trace Contact",
            email = $"trace-{Guid.NewGuid():N}@example.com",
            phone = "+963900000001",
            role = "Primary",
        });
        response.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuditLogs.Where(a => a.CorrelationId == expected).FirstOrDefaultAsync();

        row.Should().NotBeNull(
            "the audit row's correlation id must BE the request's trace id - that is what makes an " +
            "audit entry joinable to its trace (NFR-OBS-003, FR-AUD-005), and no locally generated " +
            "id can satisfy it");
    }

    [Fact]
    public async Task Two_audit_rows_from_one_request_share_a_correlation_id()
    {
        // This is the check that separates a real fix from relocating the Guid.NewGuid(). A logger
        // that generated its own id per call would still produce populated, unique values and would
        // still fail here.
        var (header, expected) = TraceParent();
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Two Rows Co");

        // An approved supplier's bank-account change writes both bank_account_added AND
        // compliance_field_changed_review_retriggered in a single request.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.OrderByDescending(s => s.CreatedAt).FirstAsync();
            await db.Suppliers.Where(s => s.Id == supplier.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved));
        }

        client.DefaultRequestHeaders.Remove("traceparent");
        client.DefaultRequestHeaders.Add("traceparent", header);

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/bank-accounts", new
        {
            accountHolderName = "Two Rows Co",
            bankName = "Test Bank",
            branchName = "Main",
            accountNumber = "SY0000000000000000000001",
            swiftBic = (string?)null,
            currencyCode = "SYP",
        });
        response.EnsureSuccessStatusCode();

        await using var verify = fixture.Services.CreateAsyncScope();
        var vdb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await vdb.AuditLogs.Where(a => a.CorrelationId == expected).ToListAsync();

        rows.Should().HaveCountGreaterThan(1,
            "a single request that writes several audit rows must group them under one id; if this " +
            "is 1, the request only produced one row and the test is not exercising what it claims");
        rows.Select(r => r.CorrelationId).Distinct().Should().ContainSingle(
            "every row from one request shares the request's id");
    }

    [Fact]
    public async Task Audit_row_from_a_read_only_path_is_persisted()
    {
        // The regression the three added SaveChangesAsync calls exist to prevent. AuditLogger no
        // longer saves, and a read-only handler has no save of its own, so without the added call
        // this row is silently dropped: the action succeeds, returns 200, and leaves no trace. An
        // audit trail that quietly loses entries is worse than one that never existed, because
        // people rely on it.
        var (header, expected) = TraceParent();
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Readonly Audit Co");
        client.DefaultRequestHeaders.Remove("traceparent");
        client.DefaultRequestHeaders.Add("traceparent", header);

        // Invite is the read-only-ish path with no save of its own: UserManager persists the user,
        // but the audit row lives on AppDbContext.
        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/users", new
        {
            email = $"invitee-{Guid.NewGuid():N}@example.com",
            fullName = "Invited User",
        });
        response.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.CorrelationId == expected && a.Action == "supplier_user_invited");

        row.Should().NotBeNull(
            "a handler with no SaveChangesAsync of its own must still persist its audit row");
    }

    /// <summary>
    /// The document-download path specifically, because it is the ONE of the three added saves with
    /// no compile-time guard.
    ///
    /// Removing the save from InviteSupplierUserHandler or MfaHandlers makes their `db` parameter
    /// unread, so warnings-as-errors fails the build (CS9113) before any test runs. This handler
    /// uses `db` for its query as well, so deleting its save compiles cleanly - and, verified, the
    /// entire integration suite still passed with the audit row silently gone. This test closes
    /// that gap.
    /// </summary>
    [Fact]
    public async Task Document_download_writes_a_persisted_audit_row()
    {
        var (header, expected) = TraceParent();
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Download Audit Co");

        using var content = new MultipartFormDataContent
        {
            { new StringContent(DocumentUploadTests.TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            // BRULE-020 (MSP-68): tax_certificate is ExpiryTracked, so a future expiry is now
            // mandatory. This test previously uploaded without one and passed, because the rule was
            // unenforced - the upload succeeded and the document could never expire.
            { new StringContent(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "expiryDate" },
        };
        var file = new ByteArrayContent(DocumentUploadTests.MinimalPdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "cert.pdf");

        var upload = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);
        upload.EnsureSuccessStatusCode();
        // T-010: the upload response's "id" is now the public code, not the Guid - §3 keeps
        // internal ids out of payloads as well as URLs.
        var documentCode = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetString()!;

        // A freshly uploaded document sits in PendingScan, which the handler refuses with 404.
        // Advance it, so the test exercises the audit path rather than the rejection path.
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var sdb = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await sdb.SupplierDocuments.Where(d => d.ReferenceCode == documentCode)
                .ExecuteUpdateAsync(p => p.SetProperty(d => d.State, DocumentState.Uploaded));
        }

        client.DefaultRequestHeaders.Remove("traceparent");
        client.DefaultRequestHeaders.Add("traceparent", header);

        var download = await client.GetAsync($"/api/v1/documents/{documentCode}/download-url");
        download.EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.CorrelationId == expected && a.Action == "document_access_granted");

        row.Should().NotBeNull(
            "a download that leaves no audit trace is the record a later review would go looking " +
            "for; this handler is read-only, so nothing else would persist the row");
    }
}
