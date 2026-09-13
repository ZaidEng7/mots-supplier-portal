// One buying body may not see another's work, across every org-scoped surface at once.
//
// Why a sweep and not more of what is already here. Five suites already assert this for the feature they are
// about - ApprovalQueuesTests for the queues, ProcurementDashboardTests for the counts, and DeadlineChangeTests,
// ProposalDocumentDownloadTests and ReportEndpointsTests each for their own. Every one of them is a good test
// and none of them is a denominator: a sixth surface added tomorrow is covered by nobody, and the way anyone
// would find out is a support ticket.
//
// This walks the buyer's org-scoped GET surfaces in one pass with one pair of organisations, so adding a
// route to the list is the whole cost of covering it. RowScopeGuardTests in the architecture suite is the
// other half: it reads the handlers and asks whether each one reaches for the caller's scope at all. This one
// asks what actually comes back over HTTP.
//
// RISK-004 is the risk register's only Critical entry and it names exactly this: "a supplier sees another
// supplier's proposal, or one buying entity sees another's RFQ". Its stated mitigation is scoping "asserted
// in a shared query pipeline, not per-endpoint ad hoc". There is no shared pipeline; seventy handlers do it
// by hand. Until there is one, this is the assertion.
//
// The surface list is typed out by hand, the way every list in this repository is, so that adding a route is
// a decision somebody takes rather than a gap that opens. Each is a GET a procurement officer or manager can
// call with no argument naming a tender, so whatever comes back is whatever the handler's own scope predicate
// let through. The one filter the list declares has the literals "me" and "unassigned" - anything else is 422
// by design, which is a §6.2 behaviour ListQueryConformanceTests already pins and not this sweep's question.
//
// The approval queues entry was written as /approvals/queues until T-031 needed the route for real and found
// it answers 404: the path is /procurement/approvals. The sweep skipped it in silence for every persona,
// because a 404 is how it recognises "this persona cannot reach this surface" and a route that exists for
// nobody looks exactly the same. The reachability guard is what makes a typo fail instead of quietly
// shrinking the sweep.
//
// The other organisation's tender is put into every state this sweep's surfaces read from, so a surface that
// filters by state still has one of theirs to leak. A 403 is not a pass: the question is what a caller who IS
// allowed to ask gets back, so a surface this persona cannot reach is skipped rather than counted as scoped.
// Non-vacuity is asserted before the assertion that uses it, because a sweep where every surface answered 403
// would otherwise report a clean pass over nothing at all - the failure mode this repository has paid for
// more than once - and the sharper version of the same worry was added after a surface in this very list
// turned out to be a path that exists for nobody: a route no persona can reach contributes nothing and says
// nothing about it, so the list silently shrinks and the count still passes. The control is that MY tender is
// visible to me on the list, so the assertion is about scoping rather than about an empty database.
//
// The second test is §9.2: another organisation's tender answers 404 rather than 403, with the same body,
// because a 403 on a code that exists confirms it exists - which is the disclosure the status code was chosen
// to avoid. The comparison strips the per-request fields, so two responses are compared for the only thing
// that matters here: whether they say the same thing about the resource.

namespace MotsSupplierPortal.Tests.Integration.Authorization;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RowScopeSweepTests(PostgresApiFixture fixture)
{
    private static readonly string[] BuyerSurfaces =
    [
        "/api/v1/rfqs",
        "/api/v1/rfqs?owner=unassigned",
        "/api/v1/procurement/dashboard",
        "/api/v1/procurement/approvals",
        "/api/v1/reports/procurement",
        "/api/v1/search?q=Sweep",
    ];

    [Fact]
    public async Task No_buyer_surface_returns_another_organizations_tender()
    {
        var theirs = await EvaluationSeed.CreateAsync(fixture, "SweepTheirs");
        var mine = await EvaluationSeed.CreateAsync(fixture, "SweepMine");

        await theirs.Officer.PostAsync($"/api/v1/rfqs/{theirs.RfqCode}/submit-review", null);

        var leaked = new List<string>();
        var unreachable = new List<string>();
        var read = 0;

        foreach (var surface in BuyerSurfaces)
        {
            foreach (var (persona, client) in new[] { ("officer", mine.Officer), ("manager", mine.Manager) })
            {
                var response = await client.GetAsync(surface);

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    unreachable.Add($"{surface} as {persona}");
                    continue;
                }

                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    $"{surface} as {persona} must answer, or this sweep is measuring an error page");

                var body = await response.Content.ReadAsStringAsync();
                read++;

                if (body.Contains(theirs.RfqCode, StringComparison.Ordinal))
                {
                    leaked.Add($"  {surface} as {persona} -> contains {theirs.RfqCode}");
                }
            }
        }

        read.Should().BeGreaterThan(5,
            "the sweep must actually be reading buyer surfaces; a pass over zero responses proves nothing");

        foreach (var surface in BuyerSurfaces)
        {
            unreachable.Count(u => u.StartsWith(surface + " as", StringComparison.Ordinal))
                .Should().BeLessThan(2,
                    $"'{surface}' answered 404 or 403 for BOTH personas, so this sweep is not sweeping it - "
                    + "either the path is wrong or no buyer can reach it");
        }

        var ownList = await mine.Officer.GetAsync("/api/v1/rfqs");
        (await ownList.Content.ReadAsStringAsync()).Should().Contain(mine.RfqCode,
            "a caller must see their own organisation's tender, or this proves only that nothing is returned");

        leaked.Should().BeEmpty(
            "a buyer surface returned another organisation's tender, which is RISK-004:\n"
            + string.Join("\n", leaked));
    }

    [Fact]
    public async Task Another_organizations_tender_is_indistinguishable_from_one_that_never_existed()
    {
        var theirs = await EvaluationSeed.CreateAsync(fixture, "SweepDetailTheirs");
        var mine = await EvaluationSeed.CreateAsync(fixture, "SweepDetailMine");

        var outOfScope = await mine.Officer.GetAsync($"/api/v1/rfqs/{theirs.RfqCode}");
        var neverExisted = await mine.Officer.GetAsync("/api/v1/rfqs/RFQ-0000-000000");

        outOfScope.StatusCode.Should().Be(HttpStatusCode.NotFound);
        neverExisted.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outOfScopeBody = Normalise(await outOfScope.Content.ReadAsStringAsync());
        var neverExistedBody = Normalise(await neverExisted.Content.ReadAsStringAsync());

        outOfScopeBody.Should().Be(neverExistedBody,
            "the two answers must be identical; a difference tells a caller which codes are real");
    }

    private static string Normalise(string body)
    {
        using var document = JsonDocument.Parse(body);
        var fields = document.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "correlationId" or "instance"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");

        return string.Join("|", fields);
    }
}
