// Two documented but previously unenforced mapper traps, both real for the same reason: a comment does not prevent a
// defect, only a build or a test does.
//
//
// TRAP ONE: A NEW CHILD MUST BE ANNOUNCED AS NEW
//
// Several child identifiers are assigned in the domain factory, so the change tracker's default inference, which
// guesses from whether the key already has a value, marks a brand-new entity as existing and emits a pointless
// update instead of an insert.
//
// Each handler works around that with an explicit add. This proves that call is load-bearing, by seeding through the
// real endpoints and re-reading through a FRESH scope rather than the same one, so the identity map cannot mask a
// write that never reached the database.
//
// The revert-to-red is described in the change itself: remove one of those calls and the corresponding collection
// comes back empty on reload.
//
//
// TRAP TWO: EVERY COLLECTION THE READ MODEL READS MUST BE LOADED
//
// The shared include's own header states the invariant: otherwise the read model silently under-reports, which
// already happened once for the representatives.
//
// This seeds one real row in EVERY one of the six collections and asserts all six come back non-empty through the
// loader. The denominator is the six collections, asserted by name rather than merely that some data exists.
//
// Registration already seeds one representative, and the other five start empty, so they are seeded through the real
// handlers, which is the path production traffic uses rather than a direct write in the test.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AggregateLoadingAndTrackingTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Every_child_collection_survives_a_real_INSERT_and_reload_through_a_fresh_scope()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Aggregate Co {Guid.NewGuid():N}"[..24]);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/suppliers/me");
        var referenceCode = me.GetProperty("supplierCode").GetString();

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/addresses", new
        {
            kind = "HeadOffice",
            line1 = "1 Aggregate Street",
            city = "Damascus",
            regionCode = "DIM",
            country = "Syria",
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/contacts", new
        {
            fullName = "Aggregate Contact",
            email = "aggregate-contact@example.com",
            phone = (string?)null,
            role = "ops",
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/branches", new
        {
            nameAr = "فرع",
            nameEn = "Aggregate Branch",
            addressId = (Guid?)null,
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/bank-accounts", new
        {
            accountHolderName = "Aggregate Holder",
            bankName = "Aggregate Bank",
            branchName = (string?)null,
            accountNumber = "1234567890",
            swiftBic = (string?)null,
            currencyCode = "SYP",
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/v1/suppliers/me/category-links", new { categoryCode = "catering" }))
            .EnsureSuccessStatusCode();

        await using var freshScope = fixture.Services.CreateAsyncScope();
        var db = freshScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Suppliers.IncludeProfile().SingleAsync(s => s.ReferenceCode == referenceCode);

        reloaded.Representatives.Should().ContainSingle("registration seeds exactly one representative");
        reloaded.Addresses.Should().ContainSingle("the address INSERT must have actually reached the database");
        reloaded.Contacts.Should().ContainSingle("the contact INSERT must have actually reached the database");
        reloaded.Branches.Should().ContainSingle("the branch INSERT must have actually reached the database");
        reloaded.BankAccounts.Should().ContainSingle("the bank account INSERT must have actually reached the database");
        reloaded.CategoryLinks.Should().ContainSingle("the category link INSERT must have actually reached the database");
    }
}
