// GET /api/v1/map/tiles/{z}/{x}/{y}.png - map imagery for the address picker, fetched by us rather than by
// the supplier's browser.
//
// WHY THE SERVER FETCHES AND NOT THE BROWSER. Pointing the map straight at openstreetmap.org would have meant
// three things we did not want. The application's own Content-Security-Policy says img-src 'self', which is the
// line that currently reads "this product loads nothing from the outside world except Google's fonts" - tiles
// would have required widening it, and a hole opened for tiles stays open for everything else on that host.
// Every supplier's browser would have told a third party its IP address and roughly which part of Syria it was
// looking at. And the map would have depended, at the moment a supplier is filling in a form, on a free service
// being reachable from their network. Proxying costs one endpoint and removes all three.
//
// IT ALSO BECOMES SELF-HOSTING FOR FREE. The day the ministry wants the tiles inside its own network - the only
// arrangement nobody can switch off - this endpoint changes where it reads from and no browser code changes at
// all.
//
// THE COORDINATES ARE BOUNDED BEFORE ANYTHING IS FETCHED. z is capped at 19, which is as far as the upstream
// tiles go, and floored at 3 because below that the whole world is four images and nobody picking an address
// needs them. x and y must fall inside 2^z, which is the only valid range for a tile at that zoom. Without
// those checks this route would take any three numbers from a caller and turn them into an outbound request,
// which is the shape of an open proxy.
//
// THE UPSTREAM HOST IS A CONSTANT. Nothing from the request reaches the URL except three integers that have
// already been range-checked, so there is no path by which a caller chooses where this server connects to.
//
// IT REQUIRES A SIGNED-IN USER. The tiles themselves are public, so this is not about the imagery - it is about
// not running an anonymous internet-fetching endpoint on a ministry API. A supplier filling in their address is
// signed in, so nothing is lost. The browser cannot put a bearer token on an <img> tag, which is why the SPA
// fetches each tile and hands the blob to the map rather than letting the map load them itself.
//
// THE USER-AGENT IS REQUIRED BY THE UPSTREAM. OpenStreetMap's tile policy refuses traffic from clients that do
// not identify themselves, and being blocked would show as a grey square with nothing in the logs to say why.
//
// TILES ARE CACHED FOR A DAY at the browser, because a tile is a picture of the ground and the ground does not
// move. Without it, panning a map re-fetches through this server every time.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Startup;

public static class MapTileEndpoints
{
    public const string HttpClientName = "osm-tiles";

    private const int MinZoom = 3;
    private const int MaxZoom = 19;

    public static void MapMapTileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/map/tiles/{z:int}/{x:int}/{y:int}.png", async (
            int z,
            int x,
            int y,
            IHttpClientFactory clientFactory,
            ILoggerFactory loggerFactory,
            HttpResponse response,
            CancellationToken ct) =>
        {
            if (z < MinZoom || z > MaxZoom) return Results.NotFound();

            var limit = 1L << z;
            if (x < 0 || y < 0 || x >= limit || y >= limit) return Results.NotFound();

            var client = clientFactory.CreateClient(HttpClientName);

            HttpResponseMessage upstream;
            try
            {
                upstream = await client.GetAsync($"{z}/{x}/{y}.png", ct);
            }
            catch (HttpRequestException ex)
            {
                loggerFactory.CreateLogger(typeof(MapTileEndpoints))
                    .LogWarning(ex, "Map tile {Z}/{X}/{Y} could not be fetched upstream.", z, x, y);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            if (!upstream.IsSuccessStatusCode)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            response.Headers.CacheControl = "public, max-age=86400";
            var bytes = await upstream.Content.ReadAsByteArrayAsync(ct);
            return Results.File(bytes, "image/png");
        })
        .RequireAuthorization()
        .RequireRateLimiting(HttpTransportRegistration.MapTileRateLimitPolicy)
        .WithName("GetMapTile")
        .WithTags("Map");
    }
}
