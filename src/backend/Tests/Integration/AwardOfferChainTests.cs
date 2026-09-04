using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-064: <c>AwardOffered</c> and <c>Declined</c> were the last two proposal states no code could
/// reach - the same class as T-051's clarification loop and T3-36's three RFQ states.
///
/// <para>§4.1's own rows: <c>Shortlisted -&gt; AwardOffered</c> ("Selected for award ... Mark as
/// award candidate"), <c>AwardOffered -&gt; Awarded</c> ("Award confirmed"), and
/// <c>AwardOffered -&gt; Declined</c> ("Supplier declines ... Free the award for alternate; RFQ
/// returns to Recommendation").</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AwardOfferChainTests(PostgresApiFixture fixture)
{
    /// <summary>Drives an RFQ to an approved award and returns the codes involved.</summary>
    private async Task<(Seeded Seeded, HttpClient Approver, string SupplierProposalCode)> ApprovedAwardAsync(string label)
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, label);

        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        Guid criterionId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            criterionId = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == seeded.EvaluationId).Select(c => c.Id).FirstAsync();
        }

        await seeded.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/scores",
            new { proposalCode = seeded.ProposalCode, criterionId, rawScore = 90m, commentAr = (string?)null, commentEn = (string?)null });
        (await seeded.Evaluator.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/consolidate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // A recommendation needs a FINALIZED evaluation (BRULE-064's own guard), not merely a
        // consolidated one.
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/finalize", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Consolidation shortlists the qualified proposal - which is the state §4.1's offer row
        // starts from, and the reason this chain is reachable at all now.
        await AssertProposalStateAsync(seeded.ProposalCode, ProposalState.Shortlisted);

        var recommend = await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalId = seeded.ProposalId,
            justificationAr = "الأفضل سعراً وجودة", justificationEn = "Best value",
        });
        recommend.StatusCode.Should().Be(HttpStatusCode.OK, await recommend.Content.ReadAsStringAsync());

        // Still Shortlisted: a recommendation is not a decision, so no offer has been made.
        await AssertProposalStateAsync(seeded.ProposalCode, ProposalState.Shortlisted);

        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/route-for-approval", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A different manager, because BRULE-073 refuses the recommender.
        var approver = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, seeded.OrgId);
        var approve = await approver.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        return (seeded, approver, seeded.ProposalCode);
    }

    private async Task AssertProposalStateAsync(string proposalCode, ProposalState expected)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actual = await db.Proposals.AsNoTracking()
            .Where(p => p.ReferenceCode == proposalCode).Select(p => p.State).FirstAsync();
        actual.Should().Be(expected);
    }

    [Fact]
    public async Task Approving_the_award_offers_it_to_the_supplier_and_executing_confirms_it()
    {
        var (seeded, approver, proposalCode) = await ApprovedAwardAsync("Offer");

        // §4.1: Shortlisted -> AwardOffered, on approve.
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proposal = await db.Proposals.AsNoTracking().FirstAsync(p => p.ReferenceCode == proposalCode);
            proposal.AwardOfferedAt.Should().NotBeNull(
                "D-21 enforces no acceptance window, so the timestamp is what makes a long-outstanding offer visible");

            // The audited event §4.1 names.
            (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == proposalCode && a.Action == "proposal.award_offered"))
                .Should().BeTrue();

            // §4.1: "Email + in-app to supplier (offer)". Asserted on the OUTBOX rather than the
            // materialised row: the outbox is where the request is durably written, and the
            // dispatcher that drains it is a background job this test does not run.
            // Materialised first: PayloadJson is a jsonb column, and Postgres has no LIKE operator
            // for jsonb - the predicate has to run in memory, not in SQL.
            var outbox = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
            outbox.Should().Contain(p => p.Contains("proposal.award_offered"),
                "the supplier is told their bid was selected");
        }

        // §4.1: AwardOffered -> Awarded, on execute. Award() had to widen to accept AwardOffered, and
        // the loser query had to widen too - without that the WINNER falls out of it and is never
        // awarded while the RFQ completes around it.
        var execute = await approver.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/execute", null);
        execute.StatusCode.Should().Be(HttpStatusCode.OK, await execute.Content.ReadAsStringAsync());

        await AssertProposalStateAsync(proposalCode, ProposalState.Awarded);

        // The award's permanent comparison snapshot must still contain the winning bid. It is taken
        // while the winner is AwardOffered, which is outside ProposalStates.InEvaluation - hence
        // UnderComparison. This assertion is the trap's tripwire.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var snapshot = await db.Awards.AsNoTracking()
                .Where(a => db.Rfqs.Any(r => r.Id == a.RfqId && r.ReferenceCode == seeded.RfqCode))
                .Select(a => a.ComparisonSnapshotJson).FirstAsync();

            snapshot.Should().NotBeNull();
            snapshot!.Should().Contain(proposalCode,
                "the award record must contain the bid it awarded - the snapshot is taken while the " +
                "winner sits in AwardOffered, so a comparison filtered on the evaluation set alone " +
                "would have recorded an award with no winning proposal in it");
        }
    }

    [Fact]
    public async Task A_supplier_can_decline_the_offer_and_the_rfq_returns_to_recommendation()
    {
        var (seeded, _, proposalCode) = await ApprovedAwardAsync("Decline");
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);

        var decline = await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "Capacity constraints this season" });

        decline.StatusCode.Should().Be(HttpStatusCode.OK, await decline.Content.ReadAsStringAsync());
        await AssertProposalStateAsync(proposalCode, ProposalState.Declined);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // §4.1: "Free the award for alternate; RFQ returns to Recommendation".
        var rfqState = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == seeded.RfqCode).Select(r => r.State).FirstAsync();
        rfqState.Should().Be(RfqState.Recommendation,
            "an offer that dies while the RFQ sits in AwardApproval leaves an officer with no route " +
            "to an alternate - which is what §4.1's effect column is for");

        // The reason is on the audit row and NOT in the notification payload (BRULE-091).
        var audit = await db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.ReferenceCode == proposalCode && a.Action == "proposal.declined");
        audit.Reason.Should().Be("Capacity constraints this season");

        // BRULE-091 is enforced when the payload is CONSTRUCTED, so the outbox row is the thing to
        // assert against - it is what was durably written down.
        var payloads = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
        var declined = payloads.Where(p => p.Contains("proposal.declined")).ToList();
        declined.Should().NotBeEmpty("§4.1: In-app to procurement");
        declined.Should().OnlyContain(p => !p.Contains("Capacity constraints"),
            "BRULE-091 keeps a supplier's free text out of the payload; the officer reads it on the screen");
    }

    [Fact]
    public async Task Declining_needs_a_reason_and_is_refused_from_any_other_state()
    {
        var (seeded, _, proposalCode) = await ApprovedAwardAsync("DeclineGuard");

        // The guard can refuse: no reason.
        var noReason = await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "" });
        // 422, not 400 - this is FluentValidation's bilingual field-errors path, the same shape
        // every other empty-reason refusal in this codebase takes.
        noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);

        // The guard can be satisfied - the control, same caller, same route.
        (await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "No capacity" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProposalStateAsync(proposalCode, ProposalState.Declined);

        // And it refuses a SECOND decline - Declined is terminal, and §3 says that answers 409 with
        // the current state and where it can go.
        var again = await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "Again" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("currentState").GetString().Should().Be(nameof(ProposalState.Declined));
        problem.GetProperty("allowedNext").GetArrayLength().Should().Be(0, "Declined is terminal");
    }

    [Fact]
    public async Task Another_supplier_cannot_decline_an_offer_that_is_not_theirs()
    {
        var (_, _, proposalCode) = await ApprovedAwardAsync("DeclineScope");

        // A supplier with no relationship to this proposal, holding the same permission.
        var (outsider, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Outsider {Guid.NewGuid():N}"[..30]);

        var response = await outsider.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "Not mine to decline" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out of scope is indistinguishable from a code that does not exist");
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);
    }
}
