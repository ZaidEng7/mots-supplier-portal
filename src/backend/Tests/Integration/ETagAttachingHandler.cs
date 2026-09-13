// Gives the test client the same precondition behaviour the interface has.
//
// So the version guard does not have to be spelled out in three hundred assertions that were written before it
// existed.
//
// It reads the current version of the owning resource and attaches it to a mutation on the same resource, walking
// up the path so a mutation on a sub-path is covered by the version of the resource that owns it.
//
//
// THIS MAKES THE GUARD INVISIBLE TO ORDINARY TESTS, WHICH IS THE POINT AND ALSO THE RISK
//
// A client that always sends a fresh version can never observe a refusal, so the concurrency tests deliberately
// bypass this handler and construct staleness by hand.
//
// What this handler proves is only that the rest of the suite is not asserting against a contract it never sends.
// The guard itself is proven where it is tested.
//
//
// THE CACHE IS GONE, AND DELIBERATELY
//
// While the version was the database's own row identifier, a child write did not move its root, so a version read
// once stayed valid until somebody wrote the root itself, and caching it per path was safe.
//
// With an application-managed version, ANY write that touches an aggregate moves it, including a write made by a
// DIFFERENT client: a reviewer rejecting a document now advances that supplier's version, and the supplier's own
// cached version is stale with no way for it to know.
//
// That is the behaviour the change exists to produce, and it is what a real client handles by re-reading after a
// refusal. A harness replaying a cached version would be asserting the OLD defect. Probing fresh costs one read
// per mutation and removes the whole class of false failure.
//
// The probe is built as an absolute address, because this handler sits below the client and the base address has
// already been applied to the real request, so a relative probe would never be resolved.
//
//
// WHEN NOTHING IS READABLE AT ANY PREFIX
//
// The resource does not exist, or is outside this caller's scope and reads as absent.
//
// It sends a well-formed version that cannot match anything rather than no header at all. Without one the request
// would stop at the precondition gate, and a cross-organization negative would prove only that the gate runs
// rather than that scoping does. With it, the request reaches the handler and the scope check answers absent,
// which is what the contract requires it to be indistinguishable from.
//
//
// A FRESH IDEMPOTENCY KEY PER MUTATION
//
// The interface generates one per user submission intent. Every mutation gets a fresh one here for the same
// reason: the suite has some hundred and forty call sites on publish, submit and approve, and each is one intent,
// so a key per request is the faithful analogue.
//
// A key REUSED across two calls would be asserting replay, which the dedicated tests do deliberately with a key
// they control.

namespace MotsSupplierPortal.Tests.Integration;

using System.Net.Http.Headers;
using System.Collections.Concurrent;

public sealed class ETagAttachingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isMutation = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;

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

        foreach (var prefix in Prefixes(uri.AbsolutePath))
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, prefix));
            foreach (var header in request.Headers) probe.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await base.SendAsync(probe, ct);
            if (response.Headers.ETag is { } tag) return tag.ToString();
        }

        return ImpossibleVersion;
    }

    private const string ImpossibleVersion = "\"AAAAAA\"";

    private static IEnumerable<string> Prefixes(string path)
    {
        var segments = path.Trim('/').Split('/');
        for (var take = segments.Length; take >= 3; take--)
        {
            yield return "/" + string.Join('/', segments.Take(take));
        }
    }
}
