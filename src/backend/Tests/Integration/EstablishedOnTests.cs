using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// A company cannot have been founded tomorrow.
///
/// <para>The onboarding form's date picker offered next month and this validator took it, so a
/// supplier could be stored - and shown in the National Supplier Registry - with a founding date
/// after today. Reported from the screen: the calendar let a date in 2026-09-22 be chosen on
/// 2026-09-13 and the save succeeded.</para>
///
/// <para>Refused at the API and not only in the form, because the form is one way in and the ERP
/// import is another.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EstablishedOnTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// A fresh registration number per call, because the column is unique across suppliers: three
    /// tests sharing one literal made the second and third fail on the constraint rather than on
    /// anything this file is about.
    /// </summary>
    private static object LegalInfo(string establishedOn) => new
    {
        legalNameAr = "شركة التاريخ",
        legalNameEn = "Founding Date Co",
        registrationNumber = $"CR-{Guid.NewGuid():N}"[..14],
        taxId = (string?)null,
        supplierType = "Company",
        establishedOn,
    };

    [Fact]
    public async Task A_founding_date_in_the_future_is_refused()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Founded {Guid.NewGuid():N}"[..30]);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)).ToString("yyyy-MM-dd");

        var response = await client.PutAsJsonAsync("/api/v1/suppliers/me/legal-info", LegalInfo(tomorrow));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a date after today describes a company that does not exist yet");
        (await response.Content.ReadAsStringAsync()).Should().Contain("future");
    }

    /// <summary>
    /// The control. Without it the test above passes against a validator that refuses every date,
    /// which would be a worse defect than the one it fixes.
    /// </summary>
    [Fact]
    public async Task A_founding_date_in_the_past_is_accepted()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Founded2 {Guid.NewGuid():N}"[..30]);

        var response = await client.PutAsJsonAsync("/api/v1/suppliers/me/legal-info", LegalInfo("2019-01-01"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Today itself is a real founding date, and an off-by-one here would refuse it.</summary>
    [Fact]
    public async Task Today_is_accepted()
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Founded3 {Guid.NewGuid():N}"[..30]);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date).ToString("yyyy-MM-dd");

        var response = await client.PutAsJsonAsync("/api/v1/suppliers/me/legal-info", LegalInfo(today));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
