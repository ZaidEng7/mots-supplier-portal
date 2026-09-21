// Coordinates on a supplier's address, refused at the server when they are not coordinates.
//
// The form checks the range too, and that check is not the guard. A browser rule is a courtesy to the person typing;
// this endpoint is reachable with curl, and until now it took any double at all - a latitude of 900 would have been
// stored, exported, and eventually plotted somewhere off the planet.
//
// The pair of tests is a refusal and an acceptance, because a validator that refused everything would pass the first
// assertion on its own. The accepted value is NEGATIVE deliberately: the southern hemisphere and the western one are
// the values a range check written as "less than 90" instead of "between -90 and 90" gets wrong, and Syria's own
// coordinates would never catch it.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class AddressCoordinateTests(PostgresApiFixture fixture)
{
    private static object Address(double? latitude, double? longitude) => new
    {
        kind = "HeadOffice",
        line1 = "1 Baghdad Street",
        line2 = (string?)null,
        city = "Damascus",
        regionCode = "DIM",
        country = "SY",
        postalCode = (string?)null,
        latitude,
        longitude,
    };

    [Fact]
    public async Task A_latitude_outside_the_world_is_refused()
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(
            fixture, $"Coord Bad {Guid.NewGuid():N}"[..20]);

        var response = await supplier.PostAsJsonAsync("/api/v1/suppliers/me/addresses", Address(900, 36.2765));

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity,
            "the form's range check is a courtesy to the person typing; this endpoint is reachable with curl, and "
            + "422 is what every other refused field on this route answers");
    }

    [Fact]
    public async Task A_southern_and_western_coordinate_is_accepted()
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(
            fixture, $"Coord Good {Guid.NewGuid():N}"[..20]);

        var response = await supplier.PostAsJsonAsync("/api/v1/suppliers/me/addresses", Address(-33.8688, -70.6693));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a range written as 'less than 90' rather than 'between -90 and 90' passes every Syrian coordinate and "
            + "fails this one, so without it the refusal above proves nothing");
    }
}
