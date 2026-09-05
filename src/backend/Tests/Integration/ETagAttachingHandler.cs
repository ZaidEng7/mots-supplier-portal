using System.Net.Http.Headers;
using System.Collections.Concurrent;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Gives the test client the same <c>If-Match</c> behaviour the SPA has, so §8.1's guard does not
/// have to be spelled out in three hundred existing assertions that were written before it existed.
///
/// <para>Reads the current <c>ETag</c> of the owning resource and attaches it to a mutation on the same
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
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isMutation = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;

        // T-053/§8.2.5: "The SPA generates one key per user submission intent ... via
        // crypto.randomUUID()". Every POST gets a fresh key here for the same reason: the suite has
        // ~140 call sites on publish/submit/approve and each of them is one user intent, so a key per
        // request is the faithful analogue. A key REUSED across two calls would be asserting replay,
        // which the dedicated idempotency tests do deliberately with a key they control.
        if (isMutation && !request.Headers.Contains("Idempotency-Key"))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        }

        if (isMutation && request.Headers.IfMatch.Count == 0)
        {
            var etag = await ResolveETagAsync(request, cancellationToken);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> ResolveETagAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { } uri) return null;

        // T-030: the cache is GONE, and deliberately.
        //
        // While the version was Postgres xmin, a child write did not move its root, so an ETag read
        // once stayed valid until someone wrote the root itself - and caching it per path was safe.
        // With an application-managed version, ANY write that touches an aggregate moves it, including
        // a write made by a DIFFERENT client: a reviewer rejecting a document now advances that
        // supplier's version, and the supplier's own cached ETag is stale with no way for it to know.
        // That is the behaviour T-030 exists to produce, and it is what a real client handles by
        // re-reading on a 412.
        //
        // A test harness replaying a cached version would be asserting the OLD defect. Probing fresh
        // costs one GET per mutation and removes the whole class of false failure.
        foreach (var prefix in Prefixes(uri.AbsolutePath))
        {
            // Absolute: this handler sits below the client, so BaseAddress has already been applied
            // to the real request and a relative probe URI would never be resolved.
            using var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, prefix));
            foreach (var header in request.Headers) probe.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await base.SendAsync(probe, ct);
            if (response.Headers.ETag is { } tag) return tag.ToString();
        }

        // Nothing readable at any prefix - the resource does not exist, or is out of this caller's
        // scope and reads as 404. Send a well-formed version that cannot match anything (xmin is
        // never 0) rather than no header at all: without it the request would stop at the 428 gate
        // and a cross-organization negative would prove only that the gate runs, not that scoping
        // does. With it, the request reaches the handler and the scope check answers 404 - which is
        // what §9.2 requires it to be indistinguishable from.
        return ImpossibleVersion;
    }

    /// <summary>base64url of uint 0. Well-formed, and never a real version: xmin was never 0, and the
    /// application-managed counter that replaced it starts at 1.</summary>
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
