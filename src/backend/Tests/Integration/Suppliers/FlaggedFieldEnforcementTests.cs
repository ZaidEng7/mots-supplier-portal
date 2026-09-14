// While a reviewer has asked for information, only the flagged fields are editable.
//
// That restriction previously existed ONLY as disabled inputs in the interface, so these tests deliberately drive
// the endpoints directly: an interface test would have passed against the broken code. Every assertion here is a
// request the browser would never send.
//
//
// THE GUARD MUST NOT LOCK THE SUPPLIER OUT OF FIXING WHAT WAS FLAGGED
//
// Which is exactly what a naive vocabulary mismatch would have caused, so that half is asserted too.
//
// And it keys off an actual value CHANGE rather than mere presence. The profile form posts all five fields every
// time, so a presence-based guard would refuse a legitimate save and lock the supplier out of correcting the
// flagged item, turning a security fix into an outage. One test flags one field and submits the whole form with only
// that field changed, every other field re-sent at its stored value.
//
// And the guard must be inert in normal states, or it would break ordinary onboarding.
//
//
// THE FIXTURE WALKS THE REAL STATE MACHINE
//
// Rather than poking the column, so it cannot drift from a state the application could actually produce. The first
// profile edit advances the onboarding state and the rest satisfies the submit gate.
//
// It loads the full profile, because without it the child collections load empty and the submit gate reports every
// requirement as missing.
//
// New children are added explicitly, because their identifiers are assigned in the domain factories, so the change
// tracker would otherwise infer an existing row and emit a pointless update, which is the same trap the production
// handler documents.
//
// It drives the domain rather than the reviewer endpoints, which need a separate staff identity that is not what is
// under test here.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class FlaggedFieldEnforcementTests(PostgresApiFixture fixture)
{
    private async Task<HttpClient> CreateSupplierInInfoRequestedAsync(string flaggedCode)
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Flagged Field Co");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var referenceCode = me.GetProperty("supplierCode").GetString();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = db.Suppliers.IncludeProfile().Single(s => s.ReferenceCode == referenceCode);

        supplier.UpdateCoreProfile("seed", null, null, "SYP");
        var seedAddress = supplier.AddAddress(AddressKind.HeadOffice, "1 Seed Street", null, "Damascus", "DIM", "Syria", null, null, null);
        db.Addresses.Add(seedAddress);
        var (seedLink, _) = supplier.LinkCategory("catering", isComplianceCritical: false);
        if (seedLink is not null) db.CategoryLinks.Add(seedLink);
        supplier.AcceptTerms(Supplier.CurrentTermsVersion);
        supplier.Submit([]);
        supplier.PickUpForReview();
        supplier.RequestInfo();

        db.SupplierReviewAnnotations.Add(new SupplierReviewAnnotation
        {
            Id = Guid.CreateVersion7(),
            SupplierId = supplier.Id,
            RequestedAt = DateTimeOffset.UtcNow,
            Reason = "Please correct the flagged section.",
            FlaggedProfileFields = [flaggedCode],
            FlaggedDocumentTypeIds = [],
        });
        await db.SaveChangesAsync();

        return client;
    }

    [Fact]
    public async Task Non_flagged_compliance_critical_field_is_refused_on_a_direct_API_call()
    {
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Address);

        var response = await client.PutAsJsonAsync("/api/v1/suppliers/me/legal-info", new
        {
            legalNameAr = "اسم جديد",
            legalNameEn = "Rewritten Legal Name",
            registrationNumber = "TAMPERED-REG",
            taxId = "TAMPERED-TAX",
            supplierType = "Company",
            establishedOn = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "legal info is compliance-critical and was not flagged - the server must refuse even " +
            "though the UI would simply have disabled the input");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("FIELD_NOT_FLAGGED");
    }

    [Fact]
    public async Task Non_flagged_bank_account_is_refused_on_a_direct_API_call()
    {
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Address);

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/bank-accounts", new
        {
            accountHolderName = "Attacker",
            bankName = "Some Bank",
            accountNumber = "111122223333",
            currencyCode = "SYP",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "bank accounts are compliance-critical and were not flagged");
    }

    [Fact]
    public async Task Non_flagged_core_profile_field_is_refused()
    {
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Address);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}")
        {
            Content = new StringContent("""{"description":"NOT-FLAGGED"}""", Encoding.UTF8, "application/json"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_flagged_section_itself_remains_editable()
    {
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Address);

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/addresses", new
        {
            kind = "HeadOffice",
            line1 = "1 Corrected Street",
            city = "Damascus",
            regionCode = "DIM",
            country = "Syria",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the supplier must be able to correct the section the reviewer actually flagged");
    }

    [Fact]
    public async Task Resending_unchanged_non_flagged_fields_alongside_a_flagged_change_is_allowed()
    {
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Description);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}")
        {
            Content = new StringContent(
                """{"description":"CORRECTED","website":null,"supplierGroup":null,"currencyCode":"SYP","primaryContactPhone":"+963900000000"}""",
                Encoding.UTF8, "application/json"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "re-sending unchanged values is not an edit of those fields");
    }

    [Fact]
    public async Task Editing_is_unrestricted_when_not_in_InfoRequested()
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Unrestricted Co");

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}")
        {
            Content = new StringContent("""{"description":"ordinary edit"}""", Encoding.UTF8, "application/json"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
