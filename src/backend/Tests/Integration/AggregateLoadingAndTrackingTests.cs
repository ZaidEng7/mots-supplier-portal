using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Task #18/MSP-88: two documented-but-previously-unenforced EF traps, both real for the same
/// reason - a comment does not prevent a defect, only a build or a test does.
///
/// <para><b>Trap 1 (ManageAddressHandler and its siblings).</b> Address/BankAccount/CategoryLink
/// ids are client-assigned (<c>Guid.CreateVersion7()</c> in the domain factory), so EF's default
/// Added-vs-Modified inference - which guesses from whether the key already has a non-default
/// value - would otherwise mark a brand-new entity Modified and emit a no-op UPDATE instead of an
/// INSERT. Each handler works around this today with an explicit <c>db.XAdd(entity)</c> call. This
/// test proves that call is load-bearing by seeding through the real HTTP endpoints and re-reading
/// through a FRESH DbContext scope (not the same one, to rule out the identity map masking a
/// failed write) - and, in the revert-to-red proof described in the PR, by removing one Add() call
/// and watching the corresponding collection come back empty on reload.</para>
///
/// <para><b>Trap 2 (IncludeProfile).</b> SupplierQueryExtensions.IncludeProfile's own comment
/// states the invariant directly: every child collection SupplierDtoMapper.ToDto reads must be
/// included, or the DTO silently under-reports (this already happened once for Representatives -
/// see that file's comment). This test seeds one real row in EVERY one of the six collections
/// IncludeProfile lists, then asserts all six come back non-empty through the loader - the
/// denominator is the six collections, asserted by name below rather than merely "some data
/// exists".</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AggregateLoadingAndTrackingTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Every_child_collection_survives_a_real_INSERT_and_reload_through_a_fresh_scope()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Aggregate Co {Guid.NewGuid():N}"[..24]);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/suppliers/me");
        var referenceCode = me.GetProperty("referenceCode").GetString();

        // Registration already seeds exactly one Representative (the registrant). The other five
        // collections start empty - seed one of each through the real handlers, the same path
        // production traffic uses, not a direct db.Add in the test.
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

        // A FRESH scope, deliberately not the one any handler above used - EF's first-level
        // (identity map) cache could otherwise hand back an in-memory object graph that looks
        // correct even if the actual INSERT never reached the database.
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
