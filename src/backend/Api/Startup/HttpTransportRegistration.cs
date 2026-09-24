// Compression, which browser origins may call this API, and the rate limits on the unauthenticated
// surface.
//
//
// COMPRESSION OVER SECURE CONNECTIONS
//
// The framework leaves compression off for secure connections by default, because of a class of attack
// that targets applications echoing attacker-controlled input back inside a response that also carries
// a fixed secret. The classic shape is a server-rendered page reflecting a query string next to an
// embedded form token: repeated probing with different guesses, watching the compressed length change,
// leaks the secret one character at a time.
//
// It is switched on here. This API is secure-only in every real environment, so leaving the default
// would make this middleware do nothing in production rather than make anything safer. And the shape
// does not fit the attack: this is a stateless API returning fixed shapes, not server-rendered pages
// reflecting free-form input beside an embedded token, and the one real secret that travels never
// appears in a compressible body at all, because it is a cookie set by a header.
//
//
// BROWSER ORIGINS
//
// There is no fallback origin list. Configuration guarantees one outside local development, and the
// local settings file supplies one there. A silent local default blocked the real interface in
// production while looking configured.
//
// The version header is explicitly exposed, and that line is load-bearing. Allowing any request header
// governs requests; a cross-origin response exposes only a small safe set of response headers to
// scripts unless it names the others, and the version header is not in that set. So every read returned
// nothing for it, the interface stored nothing, and every guarded write went out with no precondition
// and was refused. Application-wide, not only on one screen.
//
// Reproduced in a browser rather than reasoned about: clicking save on a seeded draft logged a refusal
// for a missing precondition, and the preflight for that path exposed no headers at all.
//
// It escaped notice because it fails only across origins. Served from one host, as production is, the
// header is readable and the whole concurrency layer works. Local development on two ports is the
// configuration that exposes it, and the integration tests call the API directly with no browser in the
// way, so nothing under test could ever have seen it.
//
// Credentials are allowed so the refresh cookie is sent across those two ports.
//
//
// RATE LIMITS
//
// Per network address on the unauthenticated sign-in and registration surface. Before this there was no
// rate limiting anywhere, which left signing in, forgotten passwords and registration open to
// unthrottled guessing and enumeration beyond the identity framework's own per-account lockout.
//
// Registration gets its own tighter limit, because it is more consequential per request than a sign-in
// attempt: it writes two rows and queues an email. It is applied to the registration route itself
// rather than to everything around it.
//
// The sign-in limit is configurable so the integration host can raise it. All tests share one host and
// therefore one partition, so the production default throttles the suite itself and surfaces as empty
// refusals that look like unrelated failures.
//
// A rejection is counted, tagged by request path rather than by policy name. The rejection context does
// not expose the matched policy, and the path is at least as meaningful, since it distinguishes signing
// in from registering without guessing at the framework's internal state. The counter is resolved from
// the request rather than captured, because this configuration runs before the container exists.

namespace MotsSupplierPortal.Api.Startup;

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;

internal static class HttpTransportRegistration
{
    internal const string AuthRateLimitPolicy = "auth-strict";

    internal const string RegisterRateLimitPolicy = "register-strict";

    internal const string MapTileRateLimitPolicy = "map-tiles";

    internal static WebApplicationBuilder AddHttpTransport(this WebApplicationBuilder builder)
    {
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy
                .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:5173"])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("ETag")
                .AllowCredentials()); // required so the refresh-token HttpOnly cookie is sent cross-port
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) =>
            {
                context.HttpContext.RequestServices
                    .GetRequiredService<MotsSupplierPortal.Infrastructure.Observability.AppMetrics>()
                    .RateLimitRejections.Add(1,
                        new KeyValuePair<string, object?>("surface", context.HttpContext.Request.Path.Value ?? "unknown"),
                        new KeyValuePair<string, object?>("layer", "per-ip"));
                return ValueTask.CompletedTask;
            };
            options.AddPolicy(AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10),
                        QueueLimit = 0,
                    }));
            options.AddPolicy(MapTileRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:MapTilePermitLimit", 600),
                        QueueLimit = 0,
                    }));
            options.AddPolicy(RegisterRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:RegisterPermitLimit", 5),
                        QueueLimit = 0,
                    }));
        });

        return builder;
    }
}
