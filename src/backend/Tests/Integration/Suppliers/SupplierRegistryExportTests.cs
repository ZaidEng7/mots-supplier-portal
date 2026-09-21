// The supplier registry export, over a real database and a real HTTP response.
//
//
// WHO MAY TAKE THE WHOLE REGISTRY OUT OF THE BUILDING
//
// The refusals are the substance of this file, not a formality. This one route returns every supplier in the
// country in a single file with tax identifiers, named people, their email addresses and phone numbers, and
// bank account holders. Every other read of the registry hands back one supplier, or a page of names.
//
// The procurement officer is the control that matters. That role holds supplier.directory.read and browses the
// whole national registry on screen, so a reader could reasonably assume it holds this too. It does not, and the
// test says so, because the difference between reading a directory a row at a time and carrying all of it away
// in one file is exactly the distinction the new permission exists to draw. If the export had been hung off
// supplier.directory.read - the obvious place - this test is the one that would have failed.
//
// The ministry viewer is refused for the reason audit reading was taken away from that persona: its access is to
// aggregate figures across buying bodies, and a registry export is line-level personal data.
//
// And the system administrator is asserted to SUCCEED, in the same test, because a permission matrix where
// everything is forbidden passes just as well when the route is broken or misspelled.
//
//
// THE BANK ACCOUNT NUMBER
//
// The portal stores it encrypted and shows a masked form. The export could have decrypted it - the service is
// right there - and the assertion is that it does not: the masked value is in the file and the real number is
// not, checked by searching the whole body for it rather than by reading one column, because a plaintext number
// that leaked into some other cell would pass a column-level check.
//
//
// EVERY ONBOARDING STATE
//
// A draft supplier and an approved one are both seeded and both asserted present. A file named suppliers that
// silently meant approved suppliers would be loaded into the lake as the whole registry, and every count built
// on it would be wrong in a direction nobody could see. The provenance header says every onboarding state out
// loud for the same reason, and that line is asserted too.
//
//
// THE BYTE-ORDER MARK
//
// Asserted on the raw bytes, not on the decoded string, because HttpClient decodes it away. Without it Excel
// renders the Arabic columns as mojibake and the conclusion is that the data is broken rather than that the
// viewer guessed the encoding. The Arabic name is asserted to survive the round trip in the same test.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierRegistryExportTests(PostgresApiFixture fixture)
{
    private const string Export = "/api/v1/suppliers/export";

    [Fact]
    public async Task Only_the_system_administrator_may_export_the_registry()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager);
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(
            fixture, $"Outsider {Guid.NewGuid():N}"[..20]);

        (await officer.GetAsync(Export)).StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "supplier.directory.read browses the registry a row at a time; carrying all of it away in one file "
            + "is a different disclosure and a different permission");
        (await manager.GetAsync(Export)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reviewer.GetAsync(Export)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ministry.GetAsync(Export)).StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the ministry's grant is aggregate figures; a registry export is line-level personal data");
        (await supplier.GetAsync(Export)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.GetAsync(Export)).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "without this the matrix above would pass just as well against a route that does not exist");
    }

    [Fact]
    public async Task The_file_carries_a_byte_order_mark_its_provenance_and_the_header_row()
    {
        var arabicName = $"شركة التصدير {Guid.NewGuid().ToString("N")[..6]}";
        await SeedSupplierAsync($"Export BOM {Guid.NewGuid():N}"[..20], arabicName);

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var response = await admin.GetAsync(Export);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF],
            "without the mark Excel renders the Arabic columns as mojibake, and the reader concludes the data "
            + "is broken rather than the encoding guessed");

        text.Should().Contain("# MOTS Supplier Portal - supplier registry export");
        text.Should().Contain("# scope: every supplier in the registry, at every onboarding state");
        text.Should().Contain(SupplierExportCsv.Header);
        text.Should().Contain(arabicName, "the Arabic display name has to survive the round trip");

        response.Content.Headers.ContentType!.ToString().Should().StartWith("text/csv");
    }

    [Fact]
    public async Task Every_onboarding_state_is_exported_not_only_approved_suppliers()
    {
        var draftName = $"Draft Co {Guid.NewGuid():N}"[..20];
        var approvedName = $"Appr Co {Guid.NewGuid():N}"[..20];

        var draftId = await SeedSupplierAsync(draftName, $"مسودة {Guid.NewGuid().ToString("N")[..6]}");
        var approvedId = await SeedSupplierAsync(approvedName, $"معتمدة {Guid.NewGuid().ToString("N")[..6]}");

        await SetStateAsync(draftId, SupplierOnboardingState.Draft, SupplierLifecycleState.None);
        await SetStateAsync(approvedId, SupplierOnboardingState.Approved, SupplierLifecycleState.Active);

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var text = await (await admin.GetAsync(Export)).Content.ReadAsStringAsync();

        text.Should().Contain(draftName, "a file named suppliers that silently meant approved suppliers would "
            + "be loaded into the lake as the whole registry");
        text.Should().Contain(approvedName);
    }

    [Fact]
    public async Task The_real_bank_account_number_never_reaches_the_file()
    {
        const string AccountNumber = "SY0900000000009876543210";
        var displayName = $"Bank Co {Guid.NewGuid():N}"[..20];

        var supplierClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, displayName);
        var added = await supplierClient.PostAsJsonAsync("/api/v1/suppliers/me/bank-accounts", new
        {
            accountHolderName = "Test Holder",
            bankName = "Bank of Damascus",
            branchName = "Main",
            accountNumber = AccountNumber,
            swiftBic = "SWIFTSY0",
            currencyCode = "SYP",
        });
        added.EnsureSuccessStatusCode();

        string masked;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.AsNoTracking()
                .Include(s => s.BankAccounts)
                .FirstAsync(s => s.DisplayNameEn == displayName);
            masked = supplier.BankAccounts.Single().MaskedAccountNumber;
        }

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var text = await (await admin.GetAsync(Export)).Content.ReadAsStringAsync();

        text.Should().Contain(displayName, "the supplier has to be in the file for its absence to mean anything");
        text.Should().Contain(masked);
        text.Should().NotContain(AccountNumber,
            "the whole body is searched rather than one column, because a plaintext number that leaked into "
            + "another cell would pass a column-level check");
    }

    private async Task<Guid> SeedSupplierAsync(string displayNameEn, string? displayNameAr)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, displayNameEn);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.DisplayNameEn == displayNameEn);

        if (displayNameAr is not null)
        {
            await db.Suppliers.Where(s => s.Id == supplier.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(s => s.DisplayNameAr, displayNameAr));
        }

        return supplier.Id;
    }

    private async Task SetStateAsync(Guid id, SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Suppliers.Where(s => s.Id == id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, onboarding)
            .SetProperty(s => s.LifecycleState, lifecycle));
    }
}
