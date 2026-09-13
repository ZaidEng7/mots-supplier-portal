// T-082, SCR-430 and SCR-431. The three visibility tiers, each checked in BOTH directions: that it discloses
// what it should, and that the tier below it does not. This is where a tender leaks, so no assertion here is
// about a status code alone.
//
// While the window is open the count is disclosed and no identity is. The count is already visible on the
// workspace to the same caller, so withholding it here would answer a narrower question than one already
// answered; the identities are the thing being protected, because knowing mid-tender who has bid is leverage.
// The setup puts the RFQ back into SubmissionOpen so the sealed tier is observed on an RFQ that really has a
// submitted bid - the interesting case rather than an empty one. A sealed detail is a 404 rather than an empty
// shell, because an empty shell would confirm the bid exists, which is the fact the tier protects.
//
// From SubmissionClosed the bidders and their technical content are readable. EvaluationSeed leaves the RFQ at
// UnderEvaluation with an evaluation open but NOT consolidated, which is the technical tier exactly, and the
// half that is not readable is asserted too: the commercial figures stay absent rather than zeroed. Quantity
// is technical and stays; unit price is not and goes.
//
// Commercial values appear only once the evaluation is consolidated, with the control first - before
// consolidating, the figure is absent, because asserting only the "after" would pass on a handler that never
// hid anything. The evaluation is driven to Consolidated through the real endpoints: open the workspace, which
// is what moves Assigned to InProgress, score every criterion, submit, consolidate. EvaluationSeed stops at
// UnderEvaluation with the evaluation created and nobody assigned, because assignment is SCR-500's own step
// and belongs here rather than in the shared seed.
//
// A supplier cannot read the buyer surface even holding the permission, and another organization's officer
// gets a 404 - never 403, per §9.2, because a 403 would confirm the RFQ's received proposals exist. The
// control is that the owning officer gets 200 on the same URL, so the 404 is row-scoping rather than a broken
// route.
//
// A draft proposal is never listed, because a draft is not a bid and listing one tells the buyer who is
// PREPARING to bid. It uses a DIFFERENT supplier, since unique(RfqId, SupplierId) forbids a second live
// proposal from the one that already bid - the database enforcing "one bid per supplier per RFQ" - and its
// control is that the submitted one is there, so the exclusion is about the state rather than about the list
// being empty.

namespace MotsSupplierPortal.Tests.Integration.Proposals;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class BuyerProposalReadTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> ListAsync(HttpClient client, string rfqCode)
    {
        var response = await client.GetAsync($"/api/v1/rfqs/{rfqCode}/received-proposals");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

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
        list.GetProperty("submittedCount").GetInt32().Should().BeGreaterThan(0);
        list.GetProperty("proposals").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_sealed_detail_is_a_404_rather_than_an_empty_shell()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "SealedDetail");
        await ReopenWindowAsync(seed.RfqCode);

        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{seed.ProposalId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task From_SubmissionClosed_the_bidders_and_their_technical_content_are_readable()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "TechnicalTier");

        var list = await ListAsync(seed.Officer, seed.RfqCode);
        list.GetProperty("visibility").GetString().Should().Be("Technical");
        list.GetProperty("proposals").GetArrayLength().Should().BeGreaterThan(0);

        var first = list.GetProperty("proposals")[0];
        first.GetProperty("supplierNameEn").GetString().Should().NotBeNullOrEmpty("who bid is disclosed at this tier");
        first.GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);
        first.GetProperty("currencyCode").ValueKind.Should().Be(JsonValueKind.Null);

        var detail = await seed.Officer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{seed.ProposalId}");
        detail.GetProperty("visibility").GetString().Should().Be("Technical");
        detail.GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("paymentTerms").ValueKind.Should().Be(JsonValueKind.Null);

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

        var before = await ListAsync(seed.Officer, seed.RfqCode);
        before.GetProperty("proposals")[0].GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null);

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

        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_draft_proposal_is_never_listed()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, "DraftHidden");

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

        codes.Should().NotContain(draftId);
        codes.Should().Contain(seed.ProposalId);

        (await seed.Officer.GetAsync($"/api/v1/rfqs/{seed.RfqCode}/received-proposals/{draftId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
