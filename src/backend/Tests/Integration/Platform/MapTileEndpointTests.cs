// The map tile route, which is a proxy, and is therefore tested for what it refuses rather than for what it
// fetches.
//
// A PROXY THAT TAKES THREE NUMBERS FROM A CALLER AND TURNS THEM INTO AN OUTBOUND REQUEST IS AN OPEN PROXY
// unless every one of those numbers is bounded first. These tests are the bounds: a zoom below the floor, a
// zoom above the ceiling, and a tile index outside the grid that zoom defines. Each must be refused before
// anything leaves this server, and the endpoint answers 404 rather than 400 because a tile that cannot exist
// is not there - the caller is a map library asking for a picture, not a human filling in a form.
//
// THE SIGNED-IN REQUIREMENT IS ASSERTED TOO. The imagery is public, so this is not about the pictures: it is
// about not running an anonymous internet-fetching endpoint on a ministry API. That is the kind of property
// that is added deliberately and removed by accident.
//
// THE UPSTREAM IS UNREACHABLE IN THIS SUITE, on purpose - the fixture points Map:TileBaseUrl at a closed port,
// so no test sends a build agent's traffic to OpenStreetMap. That makes the failure path the one path through
// the fetch that can be asserted, and it is the one worth asserting: a supplier whose tiles cannot be had must
// get a grey square and a logged warning, not a stack trace or a 500.
//
// THE VALID-COORDINATE TEST THEREFORE PROVES THE GUARDS LET IT THROUGH. A 502 here is the endpoint reaching the
// point of making the request, which is precisely what the 404 cases must not reach. Without this control, a
// guard bounded so tightly that it refused every real tile would pass all three refusal tests.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using System.Net;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MapTileEndpointTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task An_anonymous_caller_cannot_fetch_a_tile()
    {
        var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/map/tiles/6/38/26.png");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the tiles are public but the endpoint is not - an anonymous route that fetches from the internet " +
            "on demand is a thing this API should not be running");
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(20, 1, 1)]
    [InlineData(6, 64, 1)]
    [InlineData(6, 1, 64)]
    [InlineData(6, -1, 1)]
    public async Task A_tile_outside_the_bounds_is_refused_before_anything_is_fetched(int z, int x, int y)
    {
        var client = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        using var response = await client.GetAsync($"/api/v1/map/tiles/{z}/{x}/{y}.png");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"z={z} x={x} y={y} is not a tile that exists, and an unbounded proxy is one a caller can point " +
            "at anything");
    }

    [Fact]
    public async Task A_tile_inside_the_bounds_reaches_the_upstream_and_reports_its_failure_as_a_bad_gateway()
    {
        var client = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        using var response = await client.GetAsync("/api/v1/map/tiles/6/38/26.png");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway,
            "this suite's upstream is a closed port, so a legitimate tile must come back as a bad gateway - " +
            "which is also the control on the refusal tests above, since a guard that refused every real " +
            "tile would answer 404 here");
    }
}
