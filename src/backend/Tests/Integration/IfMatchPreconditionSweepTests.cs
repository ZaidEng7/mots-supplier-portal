using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Api.Concurrency;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Sweep: every route that demands <c>If-Match</c>, against a read that can actually supply it.
///
/// <para><b>Why this exists.</b> Five of batch 13's thirteen findings were one question - where does the
/// version come from, and does anything fetch it - with four different causes: a read that filed its ETag
/// under a path no write would look at, a write with no read beside it at all, a client helper that never
/// re-read before patching, and an aggregate whose child write had no parent read. Every one of them
/// compiled, passed the suite, and produced a 428 the first time somebody pressed the button. The common
/// property is that §8.1's contract has two halves declared in different places, and nothing had ever
/// asked whether both were present.</para>
///
/// <para><b>What the check means, precisely.</b> The SPA's ETag store (<c>api/etags.ts</c>) walks a path's
/// prefixes <b>upward, never sideways</b>: a write to <c>/a/b/c</c> can use a version filed under
/// <c>/a/b/c</c>, <c>/a/b</c> or <c>/a</c>, and never one filed under <c>/a/b/d</c>. So a guarded write is
/// satisfiable only if some ETag-emitting GET sits at its own path or a prefix of it. That is what this
/// sweep asserts against the real endpoint table - not that a particular client calls it, which is
/// <c>api/*.ts</c>'s own sweep, but that a client COULD.</para>
///
/// <para><b>Two exemption lists, named by hand.</b> Same shape as batch 12's router guard, for the same
/// reason: a pattern-matched exemption lets the next instance join it silently. Each entry below says where
/// its precondition actually comes from.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IfMatchPreconditionSweepTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// Writes whose version comes from a MUTATION's fresh ETag rather than from a GET, because the
    /// resource is created by that mutation and the client holds the version from the moment it exists.
    /// </summary>
    private static readonly Dictionary<string, string> FromAMutationResponse = new()
    {
    };

    /// <summary>
    /// Writes whose version comes from a read at a path the prefix walk cannot reach, where the client
    /// files the same ETag under both paths deliberately. Each one is a place the walk's upward-only rule
    /// had to be worked around on purpose.
    /// </summary>
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

        // The denominator, asserted before the rule. An empty set passes every assertion below while
        // checking nothing, which is the failure mode six instruments in this repository have already been
        // found in - see EndpointAuthorizationCoverageTests' own note on the same point.
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
        // The control. The matcher is the part that could pass everything: a prefix rule that answered
        // true for any pair would make the sweep above green forever. These pin both ends of it.
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

    /// <summary>Every exemption names a route that still exists. An entry for a route that has been renamed
    /// or removed is an exemption nobody is reading any more, and the next defect inherits it.</summary>
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


    /// <summary>
    /// The committed contract knows about every guarded write.
    ///
    /// <para>This is what stops the SPA's half of the sweep going quiet. That one reads
    /// <c>contracts/openapi-v1.baseline.json</c> to learn which writes need a version - so a guarded route
    /// added without refreshing the baseline would simply not be checked on the client side, and the gap
    /// would look exactly like a pass. Refresh with <c>UPDATE_OPENAPI_BASELINE=1 dotnet test</c>.</para>
    /// </summary>
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

    /// <summary>A route template as the contract spells it: no route constraints, no trailing slash.</summary>
    private static string Normalise(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{([^}:]+)(:[^}]+)?\}", "{$1}").TrimEnd('/');


    /// <summary>
    /// Writes that demand a version and do not hand back the new one - P12 item 26, T-030's fourth split,
    /// measured rather than guessed at.
    ///
    /// <para><b>Why it matters.</b> The SPA drops its cached version the moment a mutation succeeds, because a
    /// kept version is stale by definition. If the response carries no fresh ETag the next write on that
    /// aggregate has nothing to send, and the user meets a 428 on their SECOND edit - which is exactly the
    /// defect T-030's third split fixed for the supplier profile, one aggregate at a time.</para>
    ///
    /// <para>The exemptions are the honest part. A route whose response body carries no version cannot emit
    /// one, and <c>WithFreshETag</c> on it would be decoration: the filter looks for a <c>RowVersion</c>
    /// property and does nothing when there is none. Those are named below with what they return, so the list
    /// is a work item rather than an alibi - closing one means widening a DTO, which is a contract change.</para>
    /// </summary>
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

        // And the exemptions are checked in the other direction: one that HAS gained a fresh ETag is an entry
        // that should go, or the list stops describing the product.
        var stale = NoVersionOnTheResponse.Keys
            .Where(key => endpoints.Any(e =>
                $"{string.Join("/", Methods(e))} {Normalise(e.RoutePattern.RawText!)}" == key
                && e.Metadata.GetMetadata<EmitsETagMetadata>() is not null))
            .ToList();

        stale.Should().BeEmpty("these now emit a fresh ETag, so their exemptions are describing the past: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// Guarded writes whose response body carries no version, so there is nothing for a fresh ETag to be made
    /// from. Each entry says what the route returns instead - that is what a fix would have to change.
    ///
    /// <para>P12 item 26's remaining work, in one place. Every one of these is a second-edit 428 waiting for a
    /// user who does two things in a row without a re-read in between; the SPA hides it today by refetching
    /// after a mutation, which is a screen-by-screen habit rather than a property of the transport.</para>
    /// </summary>
    private static readonly Dictionary<string, string> NoVersionOnTheResponse = new(StringComparer.Ordinal)
    {
        ["POST /api/v1/suppliers/{supplierCode}/documents/{documentCode}/approve"] =
            "Returns SupplierDocumentDto, which carries no version at all - the filter looks for a RowVersion "
            + "property and would do nothing here. Closing this means putting the SUPPLIER's version on a "
            + "document response, which is a contract change and a decision about what that DTO is for. The "
            + "reviewer's screen refetches after each decision, which is why the gap has not been felt.",
        ["POST /api/v1/suppliers/{supplierCode}/documents/{documentCode}/reject"] =
            "The same DTO and the same argument as the approve above.",
    };

    private static IReadOnlyList<string> Methods(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];

    private static string[] Segments(string template) =>
        template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether a version filed under <paramref name="read"/> is reachable from a write to
    /// <paramref name="write"/> by the client's upward prefix walk.
    ///
    /// <para>A parameter segment in the READ matches anything in the same position, because at runtime the
    /// client filed its ETag under the concrete URL - <c>/suppliers/SUP-2026-000001</c> or
    /// <c>/suppliers/me</c> - and the template is only how that URL is declared. A parameter segment in the
    /// WRITE against a literal in the read is not a match: those are different URLs.</para>
    /// </summary>
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