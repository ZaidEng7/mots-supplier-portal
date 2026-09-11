using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// One buying body may not see another's work, across every org-scoped surface at once.
///
/// <para><b>Why a sweep and not more of what is already here.</b> Five suites already assert this for
/// the feature they are about - <c>ApprovalQueuesTests</c> for the queues, <c>ProcurementDashboardTests</c>
/// for the counts, <c>DeadlineChangeTests</c>, <c>ProposalDocumentDownloadTests</c> and
/// <c>ReportEndpointsTests</c> each for their own. Every one of them is a good test and none of them
/// is a denominator: a sixth surface added tomorrow is covered by nobody, and the way anyone would
/// find out is a support ticket.</para>
///
/// <para>This walks the buyer's org-scoped GET surfaces in one pass with one pair of organisations, so
/// adding a route to the list is the whole cost of covering it. <c>RowScopeGuardTests</c> in the
/// architecture suite is the other half: it reads the handlers and asks whether each one reaches for
/// the caller's scope at all. This one asks what actually comes back over HTTP.</para>
///
/// <para><b>RISK-004</b> is the risk register's only Critical entry and it names exactly this: "a
/// supplier sees another supplier's proposal, or one buying entity sees another's RFQ". Its stated
/// mitigation is scoping "asserted in a shared query pipeline, not per-endpoint ad hoc". There is no
/// shared pipeline; seventy handlers do it by hand. Until there is one, this is the assertion.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RowScopeSweepTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// The buyer surfaces that must never carry another organisation's reference code.
    ///
    /// <para>Typed out by hand, the way every list in this repository is, so that adding a route is a
    /// decision somebody takes rather than a gap that opens. Each is a GET a procurement officer or
    /// manager can call with no argument naming a tender - so whatever comes back is whatever the
    /// handler's own scope predicate let through.</para>
    /// </summary>
    private static readonly string[] BuyerSurfaces =
    [
        "/api/v1/rfqs",
        // The one filter this list declares. Its literals are "me" and "unassigned" - anything else
        // is 422 by design, which is a §6.2 behaviour ListQueryConformanceTests already pins and not
        // this sweep's question.
        "/api/v1/rfqs?owner=unassigned",
        "/api/v1/procurement/dashboard",
        "/api/v1/approvals/queues",
        "/api/v1/reports/procurement",
        "/api/v1/search?q=Sweep",
    ];

    [Fact]
    public async Task No_buyer_surface_returns_another_organizations_tender()
    {
        var theirs = await EvaluationSeed.CreateAsync(fixture, "SweepTheirs");
        var mine = await EvaluationSeed.CreateAsync(fixture, "SweepMine");

        // Their tender is put into every state this sweep's surfaces read from, so a surface that
        // filters by state still has one of theirs to leak.
        await theirs.Officer.PostAsync($"/api/v1/rfqs/{theirs.RfqCode}/submit-review", null);

        var leaked = new List<string>();
        var read = 0;

        foreach (var surface in BuyerSurfaces)
        {
            foreach (var (persona, client) in new[] { ("officer", mine.Officer), ("manager", mine.Manager) })
            {
                var response = await client.GetAsync(surface);

                // A 403 is not a pass. The question is what a caller who IS allowed to ask gets back,
                // so a surface this persona cannot reach is skipped rather than counted as scoped.
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) continue;

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

        // Non-vacuity, before the assertion that uses it. A sweep where every surface answered 403
        // would otherwise report a clean pass over nothing at all - which is the failure mode this
        // repository has paid for more than once.
        read.Should().BeGreaterThan(5,
            "the sweep must actually be reading buyer surfaces; a pass over zero responses proves nothing");

        // And the control: MY tender is visible to me on the list, so the assertion below is about
        // scoping rather than about an empty database.
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

        // §9.2: 404 rather than 403, and the same body. A 403 on a code that exists confirms it
        // exists, which is the disclosure the status code was chosen to avoid.
        outOfScope.StatusCode.Should().Be(HttpStatusCode.NotFound);
        neverExisted.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outOfScopeBody = Normalise(await outOfScope.Content.ReadAsStringAsync());
        var neverExistedBody = Normalise(await neverExisted.Content.ReadAsStringAsync());

        outOfScopeBody.Should().Be(neverExistedBody,
            "the two answers must be identical; a difference tells a caller which codes are real");
    }

    /// <summary>
    /// The problem body with its per-request fields removed, so two responses can be compared for the
    /// only thing that matters here: whether they say the same thing about the resource.
    /// </summary>
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
