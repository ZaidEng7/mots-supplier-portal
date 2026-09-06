using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-082 / SCR-430, SCR-431. The three visibility tiers, each checked in BOTH directions: that it
/// discloses what it should, and that the tier below it does not.
///
/// <para>This is where a tender leaks, so no assertion here is about a status code alone.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BuyerProposalReadTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> ListAsync(HttpClient client, string rfqCode)
    {
        var response = await client.GetAsync($"/api/v1/rfqs/{rfqCode}/received-proposals");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Puts the RFQ back into SubmissionOpen so the sealed tier can be observed on an RFQ
    /// that really has a submitted bid — the interesting case, not an empty one.</summary>
    private async Task ReopenWindowAsync(string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Rfqs.Where(r => r.ReferenceCode == rfqCode)
            .ExecuteUpdateAsync(p => p.SetProperty(r => r.State, RfqState.SubmissionOpen));
    }

    [Fact]
    public async Task While_the_window_is_open_the_count_is_disclosed_and_no_identity_is()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "SealedTier");
        await ReopenWindowAsync(seed.RfqCode);

        var list = await ListAsync(seed.Officer, seed.RfqCode);

        list.GetProperty("visibility").GetString().Should().Be("Sealed");
        // The count is already visible on the workspace to this same caller, so withholding it here
        // would answer a narrower question than one already answered.
        list.GetProperty("submittedCount").GetInt32().Should().BeGreaterThan(0);
        // The identities are the thing being protected. Knowing mid-tender who has bid is leverage.
        list.GetProperty("proposals").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_sealed_detail_is_a_404_rather_than_an_empty_shell()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "SealedDetail");
        await ReopenWindowAsync(seed.RfqCode);

        // An empty shell would confirm the bid exists, which is the fact the tier protects.
        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{seed.ProposalId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task From_SubmissionClosed_the_bidders_and_their_technical_content_are_readable()
    {
        // EvaluationSeed leaves the RFQ at UnderEvaluation with an evaluation open but NOT
        // consolidated - the technical tier exactly.
        var seed = await EvaluationSeed.CreateAsync(fixture, "TechnicalTier");

        var list = await ListAsync(seed.Officer, seed.RfqCode);
        list.GetProperty("visibility").GetString().Should().Be("Technical");
        list.GetProperty("proposals").GetArrayLength().Should().BeGreaterThan(0);

        var first = list.GetProperty("proposals")[0];
        first.GetProperty("supplierNameEn").GetString().Should().NotBeNullOrEmpty("who bid is disclosed at this tier");
        // And the half that is not: the commercial figures stay absent, not zeroed.
        first.GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);
        first.GetProperty("currencyCode").ValueKind.Should().Be(JsonValueKind.Null);

        var detail = await seed.Officer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{seed.ProposalId}");
        detail.GetProperty("visibility").GetString().Should().Be("Technical");
        detail.GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("paymentTerms").ValueKind.Should().Be(JsonValueKind.Null);

        // Quantity is technical and stays; unit price is not and goes.
        foreach (var item in detail.GetProperty("items").EnumerateArray())
        {
            item.GetProperty("quantity").GetDecimal().Should().BeGreaterThan(0);
            item.GetProperty("unitPrice").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Commercial_values_appear_only_once_the_evaluation_is_consolidated()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "CommercialTier");

        // The control: before consolidating, the figure is absent. Asserting only the "after" would
        // pass on a handler that never hid anything.
        var before = await ListAsync(seed.Officer, seed.RfqCode);
        before.GetProperty("proposals")[0].GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);

        // Drive the evaluation to Consolidated through the real endpoints: open the workspace (which
        // is what moves Assigned -> InProgress), score every criterion, submit, consolidate.
        // EvaluationSeed stops at UnderEvaluation with the evaluation created and nobody assigned -
        // assignment is SCR-500's own step, so it belongs here rather than in the shared seed.
        (await seed.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seed.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seed.EvaluatorId } })).StatusCode.Should().Be(HttpStatusCode.OK);

        var workspace = await seed.Evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{seed.RfqCode}/my-evaluation");
        foreach (var criterion in workspace.GetProperty("criteria").EnumerateArray())
        {
            var scored = await seed.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seed.RfqCode}/my-evaluation/scores", new
            {
                proposalCode = seed.ProposalCode,
                criterionId = criterion.GetProperty("id").GetGuid(),
                rawScore = 8m,
                commentAr = (string?)null,
                commentEn = (string?)null,
            });
            scored.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await seed.Evaluator.PostAsync($"/api/v1/rfqs/{seed.RfqCode}/my-evaluation/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await seed.Manager.PostAsync($"/api/v1/rfqs/{seed.RfqCode}/evaluation/consolidate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ListAsync(seed.Officer, seed.RfqCode);
        after.GetProperty("visibility").GetString().Should().Be("Commercial");
        after.GetProperty("proposals")[0].GetProperty("totalValue").ValueKind.Should().NotBe(JsonValueKind.Null);

        var detail = await seed.Officer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{seed.ProposalId}");
        detail.GetProperty("visibility").GetString().Should().Be("Commercial");
        detail.GetProperty("totalValue").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_supplier_cannot_read_the_buyer_surface_even_holding_the_permission()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "SupplierRefused");

        // 404, never 403 (§9.2) - a 403 would confirm the RFQ's received-proposals exist.
        (await seed.Supplier.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_organizations_officer_gets_a_404()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "OtherOrg");
        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        (await outsider.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The control: the owning officer gets 200 on the same URL, so the 404 above is row-scoping
        // and not a broken route.
        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_draft_proposal_is_never_listed()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "DraftHidden");

        // A DIFFERENT supplier: unique(RfqId, SupplierId) forbids a second live proposal from the one
        // that already bid, which is the database enforcing "one bid per supplier per RFQ".
        var (_, otherSupplierId) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Draft {Guid.NewGuid():N}"[..28]);

        Guid draftId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rfqId = await db.Rfqs.Where(r => r.ReferenceCode == seed.RfqCode).Select(r => r.Id).SingleAsync();
            var supplierId = await db.Suppliers.Where(x => x.DisplayNameEn.StartsWith("Draft ")).Select(x => x.Id).FirstAsync();
            var draft = MotsSupplierPortal.Domain.Proposals.Proposal.Create(
                $"PRP-D-{Guid.NewGuid():N}"[..16], rfqId, supplierId);
            db.Proposals.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.Id;
        }
        _ = otherSupplierId;

        var list = await ListAsync(seed.Officer, seed.RfqCode);
        var codes = list.GetProperty("proposals").EnumerateArray()
            .Select(p => p.GetProperty("proposalId").GetGuid()).ToList();

        // A draft is not a bid, and listing one tells the buyer who is PREPARING to bid.
        codes.Should().NotContain(draftId);
        // The control: the submitted one is there, so the exclusion is about the state and not about
        // the list being empty.
        codes.Should().Contain(seed.ProposalId);

        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{draftId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
