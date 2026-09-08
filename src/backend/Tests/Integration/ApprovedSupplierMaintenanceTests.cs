using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// What an APPROVED supplier may still maintain about itself.
///
/// <para>Every one of these answered "Cannot edit profile from state 'Approved'" before this suite
/// existed, because one guard covered every child collection on the aggregate and that guard stopped
/// at approval. Found by an approved supplier trying to add a contact, and then trying to replace a
/// tax certificate the same screen was warning them was about to expire.</para>
///
/// <para><b>The split this pins.</b> Contact-shaped data is maintainable while live: people leave and
/// offices move, and a buyer whose only named contact has gone cannot ask a clarification. Legal
/// identity and bank details are not in that set - they are what the reviewer approved and where an
/// award is paid - and they keep the behaviour they already had, which re-triggers review rather than
/// accepting the change quietly.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ApprovedSupplierMaintenanceTests(PostgresApiFixture fixture)
{
    private static readonly Guid TaxCertificateDocumentTypeId = Guid.Parse("00000000-0000-0000-0000-000000000102");

    private static readonly byte[] MinimalPdfBytes =
        "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"u8.ToArray();

    private async Task<HttpClient> ApprovedSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Suppliers.Where(s => s.DisplayNameEn == name).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return client;
    }

    [Fact]
    public async Task An_approved_supplier_can_add_a_contact()
    {
        var client = await ApprovedSupplierAsync($"Contacts {Guid.NewGuid():N}"[..24]);

        var response = await client.PostAsJsonAsync("/api/v1/suppliers/me/contacts", new
        {
            fullName = "Issam Kiswani", email = "issam@example.test", phone = "944112233", role = "CTO",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_approved_supplier_can_add_a_representative_and_an_address()
    {
        var client = await ApprovedSupplierAsync($"Reps {Guid.NewGuid():N}"[..24]);

        var rep = await client.PostAsJsonAsync("/api/v1/suppliers/me/representatives", new
        {
            fullName = "Yara Mansour", email = "yara@example.test", phone = "944112234", position = "Commercial Director",
        });
        var address = await client.PostAsJsonAsync("/api/v1/suppliers/me/addresses", new
        {
            kind = "HeadOffice", line1 = "12 Al-Thawra Street", line2 = (string?)null, city = "Damascus",
            regionCode = "DAM", country = "Syria", postalCode = "0100",
        });

        rep.StatusCode.Should().Be(HttpStatusCode.OK, await rep.Content.ReadAsStringAsync());
        address.StatusCode.Should().Be(HttpStatusCode.OK, await address.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The renewal, which is the case that mattered: expiry is tracked, a daily job expires the
    /// document, and BRULE-023 suspends the supplier for an award-critical one. The replacement lands
    /// as a NEW VERSION for a reviewer to decide on - the supplier's own onboarding state is not
    /// touched, because a company whose certificate is a year newer does not need onboarding again.
    /// </summary>
    [Fact]
    public async Task An_approved_supplier_can_upload_a_renewed_document()
    {
        var name = $"Renewal {Guid.NewGuid():N}"[..24];
        var client = await ApprovedSupplierAsync(name);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(TaxCertificateDocumentTypeId.ToString()), "documentTypeId" },
            { new StringContent("2028-06-30"), "expiryDate" },
        };
        var file = new ByteArrayContent(MinimalPdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "tax-certificate-2028.pdf");

        var response = await client.PostAsync($"/api/v1/suppliers/{await client.OwnSupplierCodeAsync()}/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("expiryDate").GetString().Should().Be("2028-06-30");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.DisplayNameEn == name);
        supplier.OnboardingState.Should().Be(SupplierOnboardingState.Approved,
            "a renewal is a document decision, not a re-onboarding");
    }

    /// <summary>
    /// The control. Widening the gate for contact details must not widen it for the two things the
    /// reviewer's approval actually rests on - and a test that only asserted the new permissions
    /// would pass just as happily if the guard had been deleted outright.
    /// </summary>
    [Fact]
    public async Task Changing_the_legal_identity_while_approved_still_returns_the_supplier_to_review()
    {
        var name = $"Legal {Guid.NewGuid():N}"[..24];
        var client = await ApprovedSupplierAsync(name);

        var response = await client.PutAsJsonAsync("/api/v1/suppliers/me/legal-info", new
        {
            legalNameAr = "شركة مختبرة", legalNameEn = "Renamed Company LLC",
            registrationNumber = "CR-2026-99999", taxId = "TAX-9999999",
            supplierType = "LimitedLiability", establishedOn = "2019-01-01",
        });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.DisplayNameEn == name);

        // Either the change is refused outright, or it is accepted and the supplier goes back for
        // review. What must never happen is a silent acceptance that leaves them Approved.
        if (response.IsSuccessStatusCode)
        {
            supplier.OnboardingState.Should().Be(SupplierOnboardingState.UnderReview,
                "a compliance-critical change re-triggers review rather than being accepted quietly");
        }
        else
        {
            supplier.OnboardingState.Should().Be(SupplierOnboardingState.Approved);
        }
    }
}
