// Who the client is, when something else received the request first.
//
// THE DEFECT THIS CLOSES
//
// Five places read Connection.RemoteIpAddress: the two rate-limit partitions, the address recorded
// against a sign-in and a refresh, and HttpAuditContext, which stamps an address on every audited
// action. With a reverse proxy in front - which is what the deployment architecture describes, with
// TLS termination and /api routing - that property is the PROXY's address on every request. Not
// sometimes: always, and identically for everyone.
//
// So the per-address rate limit degrades into one global bucket of ten requests a minute shared by
// the whole internet. It fails in both directions at once: one attacker spends the entire allowance
// and locks every legitimate user out, while never being limited as an individual. The audit trail
// degrades the same way, recording one address for every actor, which is the trail being useless at
// exactly the moment somebody asks who did something.
//
// Verified rather than reasoned about: thirteen requests to forgot-password, each carrying a
// different X-Forwarded-For and a different email address so the per-recipient limiter could not
// interfere, were counted in one bucket - ten succeeded and the rest were refused.
//
// WHY THE HEADER IS NOT SIMPLY TRUSTED
//
// Any client can send X-Forwarded-For. Honouring it unconditionally would replace a limiter that
// over-counts with one that counts nothing at all, evaded by varying a header, and would let anyone
// write whatever address they liked into the audit log. That is strictly worse than the defect
// above, because it looks like a fix.
//
// So the header is honoured only for a connection that arrives from an address named here. The list
// is deployment-specific and has no default: where nothing is configured the middleware is never
// added, the header is ignored, and the socket address stands. A direct-to-Kestrel deployment and
// local development both land in that case and are correct there.
//
// ForwardLimit is one. With a chain of addresses the rightmost entry was appended by the nearest
// proxy and is the only one that proxy vouches for; everything to its left is whatever the client
// claimed. Taking one hop from the right means a client that pre-populates the header cannot push
// its own value into the slot that gets used.
//
// The framework's own defaults for KnownProxies and KnownNetworks are cleared first. They trust
// loopback out of the box, which is a sensible default for a proxy on the same host and the wrong
// one for a list that is supposed to be exhaustive.
//
// Networks are accepted as well as single addresses, because a proxy that is a set of pods or a
// managed load balancer does not have one stable address to name.
//
// TrustsProxyHeaders is read by the pipeline to decide whether to add the middleware at all, and is
// the reason this is a class rather than two lines: the decision and the configuration it depends on
// have to agree, and they are read at different times.

namespace MotsSupplierPortal.Api.Startup;

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

internal static class ForwardedHeadersRegistration
{
    private const string ProxiesKey = "Network:TrustedProxies";

    private const string NetworksKey = "Network:TrustedProxyNetworks";

    internal static bool TrustsProxyHeaders(IConfiguration configuration) =>
        Addresses(configuration).Count > 0 || Networks(configuration).Count > 0;

    internal static WebApplicationBuilder AddForwardedHeaders(this WebApplicationBuilder builder)
    {
        if (!TrustsProxyHeaders(builder.Configuration))
        {
            return builder;
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var address in Addresses(builder.Configuration))
            {
                options.KnownProxies.Add(address);
            }

            foreach (var network in Networks(builder.Configuration))
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return builder;
    }

    private static List<IPAddress> Addresses(IConfiguration configuration) =>
        [.. Entries(configuration, ProxiesKey).Select(entry => IPAddress.Parse(entry))];

    private static List<IPNetwork> Networks(IConfiguration configuration) =>
        [.. Entries(configuration, NetworksKey).Select(Parse)];

    private static IEnumerable<string> Entries(IConfiguration configuration, string key) =>
        (configuration.GetSection(key).Get<string[]>() ?? [])
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0);

    private static IPNetwork Parse(string entry)
    {
        var parts = entry.Split('/', 2);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var length))
        {
            throw new InvalidOperationException(
                $"'{entry}' in {NetworksKey} is not a network in prefix/length form, such as 10.0.0.0/8.");
        }

        return new IPNetwork(prefix, length);
    }
}
