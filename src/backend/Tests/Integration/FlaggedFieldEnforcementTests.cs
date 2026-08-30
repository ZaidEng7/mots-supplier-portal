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

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-77 / STORY-03.3.1 AC1 / BRULE-094 / NFR-SEC-012.
///
/// While a supplier is in InfoRequested, only the reviewer's flagged fields are editable. That
/// restriction previously existed ONLY as `disabled` attributes in the SPA - so these tests
/// deliberately drive the API directly, because a UI test would have passed against the broken
/// code. Every assertion here is a request the browser would never send.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class FlaggedFieldEnforcementTests(PostgresApiFixture fixture)
{
    /// <summary>Puts the supplier into InfoRequested with exactly one flagged section, by driving
    /// the domain directly - the reviewer endpoints need a separate staff identity, which is not
    /// what is under test here.</summary>
    private async Task<HttpClient> CreateSupplierInInfoRequestedAsync(string flaggedCode)
    {
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Flagged Field Co");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        var referenceCode = me.GetProperty("referenceCode").GetString();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // IncludeProfile matters: without it the child collections load empty and the submit
        // gate reports every requirement as missing.
        var supplier = db.Suppliers.IncludeProfile().Single(s => s.ReferenceCode == referenceCode);

        // Walk the real state machine rather than poking the column, so the fixture cannot drift
        // from a state the application could actually produce. The first profile edit advances
        // EmailVerified -> ProfileInProgress; the rest satisfies the BRULE-004 submit gate.
        supplier.UpdateCoreProfile("seed", null, null, "SYP");
        // Explicit Add for both: Ids are client-assigned in the domain factories, so EF's
        // graph-tracking heuristic would otherwise infer Modified and emit a no-op UPDATE - the
        // same trap ManageAddressHandler documents.
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
        // Reviewer flagged Address only. Legal info was NOT flagged.
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
        body.GetProperty("error").GetString().Should().Be("field_not_flagged");
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

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/suppliers/me/profile")
        {
            Content = new StringContent("""{"description":"NOT-FLAGGED"}""", Encoding.UTF8, "application/json"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_flagged_section_itself_remains_editable()
    {
        // The other half: the guard must not lock the supplier out of fixing what was flagged,
        // which is exactly what a naive vocabulary mismatch would have caused.
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
        // The SPA's profile form posts all five fields every time. If the guard keyed off mere
        // presence rather than an actual value change, a legitimate save would be refused and the
        // supplier locked out of correcting the flagged item - turning a security fix into an
        // outage. Flag `description`, then submit the whole form with only description changed.
        var client = await CreateSupplierInInfoRequestedAsync(ProfileFieldCodes.Description);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/suppliers/me/profile")
        {
            // description differs; every other field is re-sent at its stored value.
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
        // The guard must be inert in normal states, or it would break ordinary onboarding.
        var client = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Unrestricted Co");

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/suppliers/me/profile")
        {
            Content = new StringContent("""{"description":"ordinary edit"}""", Encoding.UTF8, "application/json"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
