// Sweep: every route that demands If-Match, against a read that can actually supply it.
//
// Why this exists. Five of batch 13's thirteen findings were one question - where does the version come
// from, and does anything fetch it - with four different causes: a read that filed its ETag under a path no
// write would look at, a write with no read beside it at all, a client helper that never re-read before
// patching, and an aggregate whose child write had no parent read. Every one of them compiled, passed the
// suite, and produced a 428 the first time somebody pressed the button. The common property is that §8.1's
// contract has two halves declared in different places, and nothing had ever asked whether both were
// present.
//
// What the check means, precisely. The SPA's ETag store in api/etags.ts walks a path's prefixes UPWARD,
// never sideways: a write to /a/b/c can use a version filed under /a/b/c, /a/b or /a, and never one filed
// under /a/b/d. So a guarded write is satisfiable only if some ETag-emitting GET sits at its own path or a
// prefix of it. That is what this sweep asserts against the real endpoint table - not that a particular
// client calls it, which is api/*.ts's own sweep, but that a client COULD. The matcher below encodes the
// walk: a parameter segment in the READ matches anything in the same position, because at runtime the
// client filed its ETag under the concrete URL - /suppliers/SUP-2026-000001 or /suppliers/me - and the
// template is only how that URL is declared; a parameter segment in the WRITE against a literal in the read
// is not a match, because those are different URLs.
//
// Two exemption lists, named by hand, in the same shape as batch 12's router guard and for the same reason:
// a pattern-matched exemption lets the next instance join it silently, so each entry says where its
// precondition actually comes from. The first list is writes whose version comes from a MUTATION's fresh
// ETag rather than from a GET, because the resource is created by that mutation and the client holds the
// version from the moment it exists. The second is writes whose version comes from a read at a path the
// prefix walk cannot reach, where the client files the same ETag under both paths deliberately - each one a
// place the upward-only rule had to be worked around on purpose.
//
// The sweep asserts its denominator before its rule. An empty set passes every assertion while checking
// nothing, which is the failure mode six instruments in this repository have already been found in - see
// EndpointAuthorizationCoverageTests' own note on the same point. The matcher gets a control of its own,
// because it is the part that could pass everything: a prefix rule that answered true for any pair would
// make the sweep green forever, so both ends of it are pinned. And every exemption has to name a route that
// still exists, since an entry for a route that has been renamed or removed is an exemption nobody is
// reading any more, and the next defect inherits it.
//
// The committed contract has to know about every guarded write, and that is what stops the SPA's half of
// the sweep going quiet: that one reads contracts/openapi-v1.baseline.json to learn which writes need a
// version, so a guarded route added without refreshing the baseline would simply not be checked on the
// client side, and the gap would look exactly like a pass. Refresh with UPDATE_OPENAPI_BASELINE=1 dotnet
// test. Route templates are compared as the contract spells them: no route constraints, no trailing slash.
//
// The last sweep is writes that demand a version and do not hand back the new one - P12 item 26, T-030's
// fourth split, measured rather than guessed at. It matters because the SPA drops its cached version the
// moment a mutation succeeds, a kept version being stale by definition: if the response carries no fresh
// ETag the next write on that aggregate has nothing to send, and the user meets a 428 on their SECOND edit,
// which is exactly the defect T-030's third split fixed for the supplier profile one aggregate at a time.
// Its exemptions are the honest part - a route whose response body carries no version cannot emit one, and
// WithFreshETag on it would be decoration, since the filter looks for a RowVersion property and does
// nothing when there is none - so they are named with what they return, making the list a work item rather
// than an alibi: closing one means widening a DTO, which is a contract change. That dictionary is EMPTY,
// and that is the point of leaving it here: P12 item 26 closed its last two entries, the document
// approve/reject pair, by putting the SUPPLIER's version on the ETag header rather than into the document
// body, which is what RequireIfMatch on those routes guards anyway. It stays so the next guarded write that
// cannot answer with a version has to be written down here, with its reason, instead of quietly failing the
// sweep. The exemptions are also checked in the other direction: one that HAS gained a fresh ETag is an
// entry that should go, or the list stops describing the product.

namespace MotsSupplierPortal.Tests.Integration.Concurrency;

using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Concurrency;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class IfMatchPreconditionSweepTests(PostgresApiFixture fixture)
{
    private static readonly Dictionary<string, string> FromAMutationResponse = new()
    {
    };

    private static readonly Dictionary<string, string> FiledUnderASecondPathByTheClient = new()
    {
    };

    [Fact]
    public void Every_guarded_write_has_a_read_that_can_supply_its_precondition()
    {
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>().ToList();

        var guardedWrites = endpoints
            .Where(e => e.Metadata.GetMetadata<RequiresIfMatchMetadata>() is not null)
            .ToList();

        guardedWrites.Should().HaveCountGreaterThan(30,
            "§8.1 guards most of the mutation surface; a small number here means the marker is not being " +
            "applied and the sweep is passing over nothing");

        var etagReads = endpoints
            .Where(e => e.Metadata.GetMetadata<EmitsETagMetadata>() is not null)
            .Where(e => Methods(e).Contains("GET"))
            .Select(e => Segments(e.RoutePattern.RawText!))
            .ToList();

        etagReads.Should().HaveCountGreaterThan(10, "the reads are the other half of the check");

        var unsatisfiable = guardedWrites
            .Select(e => $"{string.Join("/", Methods(e))} {e.RoutePattern.RawText}")
            .Where(key => !FromAMutationResponse.ContainsKey(key))
            .Where(key => !FiledUnderASecondPathByTheClient.ContainsKey(key))
            .Where(key => !etagReads.Any(read => IsPrefixOf(read, Segments(key.Split(' ')[1]))))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        unsatisfiable.Should().BeEmpty(
            "these writes demand a version no ETag-emitting GET can hand a client, because the SPA's "
            + "prefix walk goes upward and never sideways:\n  " + string.Join("\n  ", unsatisfiable));
    }

    [Fact]
    public void The_check_can_fail()
    {
        IsPrefixOf(Segments("/api/v1/suppliers/me"), Segments("/api/v1/suppliers/me/contacts"))
            .Should().BeTrue("the walk climbs - a contact write can use the profile's own version");
        IsPrefixOf(Segments("/api/v1/suppliers/{code}"), Segments("/api/v1/suppliers/me/contacts"))
            .Should().BeTrue("a parameter segment in the read matches the literal a client actually used");
        IsPrefixOf(Segments("/api/v1/rfqs/{code}/items"), Segments("/api/v1/rfqs/{code}/requirements"))
            .Should().BeFalse("sideways is exactly what the store cannot do");
        IsPrefixOf(Segments("/api/v1/suppliers/me/contacts"), Segments("/api/v1/suppliers/me"))
            .Should().BeFalse("a deeper read does not cover a shallower write");
        IsPrefixOf(Segments("/api/v1/proposals/{code}"), Segments("/api/v1/rfqs/{code}/proposals"))
            .Should().BeFalse("two paths naming one resource is the batch-13 defect, not a match");
    }

    [Fact]
    public void No_exemption_names_a_route_that_is_gone()
    {
        var guarded = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiresIfMatchMetadata>() is not null)
            .Select(e => $"{string.Join("/", Methods(e))} {e.RoutePattern.RawText}")
            .ToHashSet(StringComparer.Ordinal);

        var stale = FromAMutationResponse.Keys.Concat(FiledUnderASecondPathByTheClient.Keys)
            .Where(key => !guarded.Contains(key)).ToList();

        stale.Should().BeEmpty("exempted routes that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public async Task The_committed_contract_documents_every_guarded_write()
    {
        var guarded = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiresIfMatchMetadata>() is not null)
            .SelectMany(e => Methods(e).Select(method => $"{method.ToLowerInvariant()} {Normalise(e.RoutePattern.RawText!)}"))
            .ToHashSet(StringComparer.Ordinal);

        var baseline = System.Text.Json.Nodes.JsonNode
            .Parse(await File.ReadAllTextAsync(BaselinePath()))!.AsObject();

        var documented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, pathItem) in baseline["paths"]!.AsObject())
        {
            foreach (var (method, operation) in pathItem!.AsObject())
            {
                var parameters = operation?["parameters"]?.AsArray() ?? [];
                if (parameters.Any(p => p?["in"]?.GetValue<string>() == "header"
                                        && p?["name"]?.GetValue<string>() == "If-Match"))
                {
                    documented.Add($"{method.ToLowerInvariant()} {Normalise(path)}");
                }
            }
        }

        guarded.Should().NotBeEmpty();
        documented.Should().NotBeEmpty("the baseline must carry the precondition for the SPA's sweep to read it");

        var missing = guarded.Except(documented).OrderBy(x => x, StringComparer.Ordinal).ToList();
        missing.Should().BeEmpty(
            "these routes demand If-Match and the committed contract does not say so, which also means the "
            + "SPA's own sweep is not checking them:\n  " + string.Join("\n  ", missing));
    }

    private static string BaselinePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "contracts", "openapi-v1.baseline.json")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the committed baseline must be findable from the test binaries");
        return Path.Combine(directory!.FullName, "contracts", "openapi-v1.baseline.json");
    }

    private static string Normalise(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{([^}:]+)(:[^}]+)?\}", "{$1}").TrimEnd('/');

    [Fact]
    public void Every_guarded_write_that_can_return_a_fresh_version_does()
    {
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiresIfMatchMetadata>() is not null)
            .ToList();

        endpoints.Should().HaveCountGreaterThan(30);

        var missing = endpoints
            .Where(e => e.Metadata.GetMetadata<EmitsETagMetadata>() is null)
            .Select(e => $"{string.Join("/", Methods(e))} {Normalise(e.RoutePattern.RawText!)}")
            .Where(key => !NoVersionOnTheResponse.ContainsKey(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "these writes demand a precondition and return no new version, so a second edit on the same "
            + "aggregate has nothing to send:\n  " + string.Join("\n  ", missing));

        var stale = NoVersionOnTheResponse.Keys
            .Where(key => endpoints.Any(e =>
                $"{string.Join("/", Methods(e))} {Normalise(e.RoutePattern.RawText!)}" == key
                && e.Metadata.GetMetadata<EmitsETagMetadata>() is not null))
            .ToList();

        stale.Should().BeEmpty("these now emit a fresh ETag, so their exemptions are describing the past: "
            + string.Join(", ", stale));
    }

    private static readonly Dictionary<string, string> NoVersionOnTheResponse = new(StringComparer.Ordinal)
    {
    };

    private static IReadOnlyList<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];

    private static string[] Segments(string template) =>
        template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsPrefixOf(string[] read, string[] write)
    {
        if (read.Length > write.Length) return false;

        for (var i = 0; i < read.Length; i++)
        {
            var readSegment = read[i];
            var writeSegment = write[i];

            if (readSegment.StartsWith('{')) continue;
            if (!string.Equals(readSegment, writeSegment, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
