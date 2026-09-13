// The two documented profile fields that nothing emitted.
//
// The document summary was absent entirely. A supplier could read a completeness fraction and a list of incomplete
// type codes, and had no count of what was approved, waiting or refused, which are the three questions a person
// actually asks while assembling an application.
//
// The modification timestamp was absent because nothing stored it. The record carried a creation time and a row
// version, and a version answers "has this changed since I read it" without answering "when".
//
//
// THE COUNTS ARE ASSERTED AGAINST THE RULE, NOT AGAINST THE SEED
//
// The seeded required set is small, so a test that wanted one document per state would be asserting against the seed
// rather than against the rule. Each test files what fits and says so.
//
// The denominator is the whole set this supplier must hold, so the counted states fit inside it and the remainder is
// what is still missing.
//
// The distinction the field exists for is asserted: refused and waiting-on-a-reviewer are the same to a supplier who
// only sees a completeness fraction, and they call for opposite actions.
//
// And the control keeps the field honest: zeroes against a non-zero requirement say "you have sent nothing and you
// need this many", where a response that omitted the field, or reported the requirement as zero, would say the
// opposite.
//
//
// THE TIMESTAMP IS ASSERTED ON OR AFTER, AND THROUGH A REAL EDIT
//
// On or after rather than equal, because the factory sets both from one clock read and registration then writes to
// the supplier again, verifying the address and recording the representative, so a freshly registered supplier is
// legitimately a few milliseconds modified. Equality would be asserting that registration is a single write, which
// it is not.
//
// The edit goes through the interface rather than the database, because the stamp has to come from the same
// mechanism that advances the row version and a direct write would bypass both.
//
// And the half that would make the field useless if it failed: a timestamp that moved on a READ would tell a client
// their copy is stale every time they check.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierProfileResponseFieldsTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Client, Guid SupplierId)> SupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        return (client, supplier.Id);
    }

    private async Task<JsonElement> ProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/suppliers/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task FileDocumentsAsync(Guid supplierId, params DocumentState[] states)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var requiredTypeIds = await db.DocumentTypes
            .Where(t => t.IsRequired && t.IsActive)
            .OrderBy(t => t.Code)
            .Select(t => t.Id)
            .ToListAsync();

        requiredTypeIds.Count.Should().BeGreaterThanOrEqualTo(states.Length,
            "the seeded required set must be big enough to carry one document per state under test");

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        foreach (var (typeId, state) in requiredTypeIds.Zip(states))
        {
            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplierId, typeId, 1, "quarantine/key",
                $"summary-{Guid.NewGuid():N}.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
                issueDate: null, expiryDate: null, expiryTracked: false, today: today);

            if (state != DocumentState.PendingScan)
            {
                document.MarkScanClean("clean/key");
                if (state == DocumentState.Approved) document.Approve(Guid.CreateVersion7());
                else if (state == DocumentState.Rejected) document.Reject(Guid.CreateVersion7(), "Illegible");
            }

            db.SupplierDocuments.Add(document);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_documents_summary_counts_the_required_set_by_the_state_of_its_latest_version()
    {
        var (client, supplierId) = await SupplierAsync($"DocSum {Guid.NewGuid():N}"[..30]);

        await FileDocumentsAsync(supplierId, DocumentState.Approved, DocumentState.Uploaded);

        var summary = (await ProfileAsync(client)).GetProperty("documentsSummary");

        summary.GetProperty("approved").GetInt32().Should().Be(1);
        summary.GetProperty("pending").GetInt32().Should().Be(1);
        summary.GetProperty("rejected").GetInt32().Should().Be(0, "nothing was refused");

        summary.GetProperty("required").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_refused_document_is_counted_as_refused_and_not_as_waiting()
    {
        var (client, supplierId) = await SupplierAsync($"DocRej {Guid.NewGuid():N}"[..30]);

        await FileDocumentsAsync(supplierId, DocumentState.Rejected);

        var summary = (await ProfileAsync(client)).GetProperty("documentsSummary");

        summary.GetProperty("rejected").GetInt32().Should().Be(1);
        summary.GetProperty("pending").GetInt32().Should().Be(0);
        summary.GetProperty("approved").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_supplier_who_has_filed_nothing_reads_a_summary_of_zeros_under_a_real_requirement()
    {
        var (client, _) = await SupplierAsync($"DocNone {Guid.NewGuid():N}"[..30]);

        var summary = (await ProfileAsync(client)).GetProperty("documentsSummary");

        summary.GetProperty("required").GetInt32().Should().BeGreaterThan(0);
        summary.GetProperty("approved").GetInt32().Should().Be(0);
        summary.GetProperty("pending").GetInt32().Should().Be(0);
        summary.GetProperty("rejected").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task The_profile_carries_an_updated_at_that_starts_equal_to_created_at_and_moves_on_a_write()
    {
        var (client, supplierId) = await SupplierAsync($"UpdAt {Guid.NewGuid():N}"[..30]);

        var before = (await ProfileAsync(client)).GetProperty("updatedAt").GetDateTimeOffset();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var created = await db.Suppliers.AsNoTracking()
                .Where(s => s.Id == supplierId).Select(s => s.CreatedAt).FirstAsync();

            before.Should().BeOnOrAfter(created,
                "a supplier cannot have been modified before it existed");
        }

        var supplierCode = (await ProfileAsync(client)).GetProperty("supplierCode").GetString();
        var edit = await client.PatchAsJsonAsync($"/api/v1/suppliers/{supplierCode}", new { description = "Edited" });
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());

        var after = (await ProfileAsync(client)).GetProperty("updatedAt").GetDateTimeOffset();
        after.Should().BeAfter(before, "an edit moves it; that is the whole field");
    }

    [Fact]
    public async Task Reading_the_profile_does_not_move_updated_at()
    {
        var (client, _) = await SupplierAsync($"UpdRead {Guid.NewGuid():N}"[..30]);

        var first = (await ProfileAsync(client)).GetProperty("updatedAt").GetDateTimeOffset();
        var second = (await ProfileAsync(client)).GetProperty("updatedAt").GetDateTimeOffset();

        second.Should().Be(first);
    }
}
