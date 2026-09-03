using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// FR-AUD-003: a supplier exports their own activity trail.
///
/// <para>The property under test is not "the export works" but "the export is scoped exactly as the
/// list is". An export that applies a different scope from the list it exports is the leak no
/// list-level test can see: every assertion about the list keeps passing while the file hands over
/// somebody else's rows.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OwnAuditTrailExportTests(PostgresApiFixture fixture)
{
    private sealed record AuditEntry(Guid Id, string Action);
    private sealed record AuditPage(List<AuditEntry> Data);

    /// <summary>Seeds a marked row on the most recently created supplier and returns its action.</summary>
    private async Task<string> SeedRowAsync(string tag)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplierId = await db.Suppliers.OrderByDescending(s => s.CreatedAt).Select(s => s.Id).FirstAsync();

        var action = $"trail_probe_{tag}_{Guid.NewGuid():N}"[..40];
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorKind = AuditActorKind.System,
            AggregateType = "Supplier",
            AggregateId = supplierId,
            Action = action,
            CorrelationId = Guid.CreateVersion7(),
        });
        await db.SaveChangesAsync();
        return action;
    }

    private static async Task<string> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/suppliers/me/audit/export");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith(new byte[] { 0xEF, 0xBB, 0xBF }, "an Arabic trail without a BOM is mojibake in Excel");
        return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    [Fact]
    public async Task A_supplier_exports_their_own_rows_and_not_another_suppliers()
    {
        // Two suppliers, each with a row nobody else should ever see. Created in this order so the
        // "most recent supplier" seed helper attaches each row to the right one.
        var supplierA = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Trail Export A");
        var actionA = await SeedRowAsync("a");

        var supplierB = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Trail Export B");
        var actionB = await SeedRowAsync("b");

        var exportA = await ExportAsync(supplierA);

        // The owner control. Without it the cross-scope assertion below passes on an export that
        // returns nothing at all, which is the failure mode a negative-only test cannot tell apart
        // from a working scope.
        exportA.Should().Contain(actionA, "control: the owner's own row is in their own export");

        exportA.Should().NotContain(actionB,
            "another supplier's row must not be in this file - and no list-level test would notice");

        // And the same in the other direction, so this is a scope and not an ordering accident.
        var exportB = await ExportAsync(supplierB);
        exportB.Should().Contain(actionB);
        exportB.Should().NotContain(actionA);
    }

    [Fact]
    public async Task The_export_returns_the_same_rows_the_list_returns()
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Trail Export Parity");
        for (var i = 0; i < 5; i++) await SeedRowAsync($"p{i}");

        var list = await supplier.GetFromJsonAsync<AuditPage>(
            "/api/v1/suppliers/me/audit?pageSize=100", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        list!.Data.Should().NotBeEmpty("control: the list this is compared against is not empty");

        var export = await ExportAsync(supplier);

        // Every row the list shows is in the file. The export is "everything in scope", not "the
        // current page", so the list being a subset is the expected direction.
        foreach (var entry in list.Data)
        {
            export.Should().Contain(entry.Id.ToString(), "a row the list shows must be in the export of that list");
        }
    }

    [Fact]
    public async Task A_staff_caller_gets_an_empty_trail_rather_than_the_whole_audit_table()
    {
        // The trap this endpoint's separate scope check exists for. The staff search's scoping is
        // deliberately unrestricted for a caller with no SupplierId, which is correct there and
        // catastrophic here: this route is gated on nothing but being signed in, so reusing that
        // scoping would export the entire audit table to any authenticated staff user.
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Trail Export Staff");
        var supplierAction = await SeedRowAsync("staffprobe");

        // Control: the row exists and IS exportable - by the supplier who owns it.
        (await ExportAsync(supplier)).Should().Contain(supplierAction);

        var staff = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var staffExport = await ExportAsync(staff);

        staffExport.Should().NotContain(supplierAction,
            "'own trail' is meaningless without a supplier scope, and answering with the global log " +
            "would hand a staff caller an unfiltered dump from a route that checks no permission");

        // Empty of DATA, not empty of file: the provenance block still states whose scope produced
        // it, so a reader can tell "nothing to show" from "the export broke".
        staffExport.Should().Contain("# scope: one supplier's own activity trail (FR-AUD-003)");
    }

    [Fact]
    public async Task The_export_is_refused_to_an_anonymous_caller()
    {
        var anonymous = fixture.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/suppliers/me/audit/export");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
