using System.Net.Http.Headers;
using System.Collections.Concurrent;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Gives the test client the same <c>If-Match</c> behaviour the SPA has, so §8.1's guard does not
/// have to be spelled out in three hundred existing assertions that were written before it existed.
///
/// <para>Remembers the <c>ETag</c> of every read, and attaches it to a later mutation on the same
/// resource - walking up the path, so a <c>POST /proposals/{code}/submit</c> is covered by the ETag
/// of <c>GET /proposals/{code}</c>. When nothing is cached it fetches the owning resource once.</para>
///
/// <para><b>This makes the guard invisible to ordinary tests, which is the point and also the
/// risk.</b> A client that always sends a fresh version can never observe a 412, so the concurrency
/// tests deliberately bypass this handler and construct staleness by hand - see
/// ConcurrencyContractTests. What this handler proves is only that the rest of the suite is not
/// asserting against a contract it never sends; the guard itself is proven where it is tested.</para>
/// </summary>
public sealed class ETagAttachingHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, string> _etags = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isMutation = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;

        if (isMutation && request.Headers.IfMatch.Count == 0)
        {
            var etag = await ResolveETagAsync(request, cancellationToken);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        var response = await base.SendAsync(request, cancellationToken);
        Remember(request, response);

        // A mutation moves the resource on, so the version just cached for it is now stale. Dropping
        // it forces the next mutation to re-read rather than replay a version the row no longer has.
        if (isMutation) Forget(request);

        return response;
    }

    private void Remember(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (response.Headers.ETag is { } tag && request.RequestUri is { } uri)
        {
            _etags[uri.AbsolutePath] = tag.ToString();
        }
    }

    private void Forget(HttpRequestMessage request)
    {
        if (request.RequestUri is not { } uri) return;
        foreach (var prefix in Prefixes(uri.AbsolutePath)) _etags.TryRemove(prefix, out _);
    }

    private async Task<string?> ResolveETagAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { } uri) return null;

        foreach (var prefix in Prefixes(uri.AbsolutePath))
        {
            if (_etags.TryGetValue(prefix, out var cached)) return cached;
        }

        foreach (var prefix in Prefixes(uri.AbsolutePath))
        {
            // Absolute: this handler sits below the client, so BaseAddress has already been applied
            // to the real request and a relative probe URI would never be resolved.
            using var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, prefix));
            foreach (var header in request.Headers) probe.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await base.SendAsync(probe, ct);
            if (response.Headers.ETag is { } tag)
            {
                _etags[prefix] = tag.ToString();
                return tag.ToString();
            }
        }

        // Nothing readable at any prefix - the resource does not exist, or is out of this caller's
        // scope and reads as 404. Send a well-formed version that cannot match anything (xmin is
        // never 0) rather than no header at all: without it the request would stop at the 428 gate
        // and a cross-organization negative would prove only that the gate runs, not that scoping
        // does. With it, the request reaches the handler and the scope check answers 404 - which is
        // what §9.2 requires it to be indistinguishable from.
        return ImpossibleVersion;
    }

    /// <summary>base64url of uint 0. Well-formed, and never a real Postgres xmin.</summary>
    private const string ImpossibleVersion = "\"AAAAAA\"";

    /// <summary>The candidate owning-resource paths, longest first.</summary>
    private static IEnumerable<string> Prefixes(string path)
    {
        var segments = path.Trim('/').Split('/');
        for (var take = segments.Length; take >= 3; take--)
        {
            yield return "/" + string.Join('/', segments.Take(take));
        }
    }
}
