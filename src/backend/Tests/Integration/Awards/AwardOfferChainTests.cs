// T-064: AwardOffered and Declined were the last two proposal states no code could reach - the same class as
// T-051's clarification loop and T3-36's three RFQ states.
//
// §4.1's own rows: Shortlisted -> AwardOffered ("Selected for award ... Mark as award candidate"),
// AwardOffered -> Awarded ("Award confirmed"), and AwardOffered -> Declined ("Supplier declines ... Free the
// award for alternate; RFQ returns to Recommendation").
//
// The setup drives an RFQ to an approved award. A recommendation needs a FINALIZED evaluation, which is
// BRULE-064's own guard, rather than merely a consolidated one, and consolidation shortlists the qualified
// proposal - the state §4.1's offer row starts from, and the reason this chain is reachable at all now. After
// the recommendation the proposal is still Shortlisted, because a recommendation is not a decision and no
// offer has been made. The approval uses a different manager, because BRULE-073 refuses the recommender.
//
// Approving offers the award to the supplier and executing confirms it. §4.1: Shortlisted -> AwardOffered on
// approve, with the audited event the table names and the "Email + in-app to supplier (offer)" notification -
// asserted on the OUTBOX rather than the materialised row, because the outbox is where the request is durably
// written and the dispatcher that drains it is a background job this test does not run. The rows are
// materialised before the predicate runs, because PayloadJson is a jsonb column and Postgres has no LIKE
// operator for jsonb, so the match has to happen in memory rather than in SQL. Then AwardOffered -> Awarded on
// execute: Award() had to widen to accept AwardOffered, and the loser query had to widen too - without that the
// WINNER falls out of it and is never awarded while the RFQ completes around it. The award's permanent
// comparison snapshot must still contain the winning bid, which is taken while the winner is AwardOffered and
// therefore outside ProposalStates.InEvaluation, hence UnderComparison: that assertion is the trap's tripwire.
//
// A supplier can decline the offer and the RFQ returns to Recommendation - §4.1's "free the award for
// alternate". The reason is on the audit row and NOT in the notification payload, per BRULE-091, which is
// enforced when the payload is CONSTRUCTED, so the outbox row is the thing to assert against: it is what was
// durably written down.
//
// Declining needs a reason and is refused from any other state. The guard can refuse - no reason, answered 422
// rather than 400, which is FluentValidation's bilingual field-errors path and the same shape every other
// empty-reason refusal in this codebase takes - and it can be satisfied, which is the control, same caller and
// same route. A SECOND decline is refused too, because Declined is terminal and §3 says that answers 409 with
// the current state and where it can go. Another supplier cannot decline an offer that is not theirs: one with
// no relationship to this proposal, holding the same permission.
//
// A-1 and BRULE-069: an unresolved tie at the top blocks a recommendation until a person resolves it. The tie
// MARKER is set in storage rather than by constructing two bids equal on every rung through thirty HTTP calls -
// the ranking arithmetic is unit-tested against a real tie in EvaluationTests, and what integration adds here
// is the award gate and the resolve endpoint, so the marker is a precondition rather than the assertion. A
// reason is mandatory: a tie broken with no stated basis is exactly what A-1 refuses to let the system do, so
// it must not be what the person does either. The resolution is asserted against storage - the marker is
// cleared, and the reason and the resolver are recorded - and the control is that with the tie resolved, the
// same recommendation now succeeds.

namespace MotsSupplierPortal.Tests.Integration.Awards;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AwardOfferChainTests(PostgresApiFixture fixture)
{
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
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/finalize", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertProposalStateAsync(seeded.ProposalCode, ProposalState.Shortlisted);

        var recommend = await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalCode = seeded.ProposalCode,
            justificationAr = "الأفضل سعراً وجودة", justificationEn = "Best value",
        });
        recommend.StatusCode.Should().Be(HttpStatusCode.OK, await recommend.Content.ReadAsStringAsync());

        await AssertProposalStateAsync(seeded.ProposalCode, ProposalState.Shortlisted);

        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/route-for-approval", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

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

        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proposal = await db.Proposals.AsNoTracking().FirstAsync(p => p.ReferenceCode == proposalCode);
            proposal.AwardOfferedAt.Should().NotBeNull(
                "D-21 enforces no acceptance window, so the timestamp is what makes a long-outstanding offer visible");

            (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == proposalCode && a.Action == "proposal.award_offered"))
                .Should().BeTrue();

            var outbox = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
            outbox.Should().Contain(p => p.Contains("proposal.award_offered"),
                "the supplier is told their bid was selected");
        }

        var execute = await approver.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/execute", null);
        execute.StatusCode.Should().Be(HttpStatusCode.OK, await execute.Content.ReadAsStringAsync());

        await AssertProposalStateAsync(proposalCode, ProposalState.Awarded);

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

        var rfqState = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == seeded.RfqCode).Select(r => r.State).FirstAsync();
        rfqState.Should().Be(RfqState.Recommendation,
            "an offer that dies while the RFQ sits in AwardApproval leaves an officer with no route " +
            "to an alternate - which is what §4.1's effect column is for");

        var audit = await db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.ReferenceCode == proposalCode && a.Action == "proposal.declined");
        audit.Reason.Should().Be("Capacity constraints this season");

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

        var noReason = await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "" });
        noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);

        (await seeded.Supplier.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "No capacity" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertProposalStateAsync(proposalCode, ProposalState.Declined);

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

        var (outsider, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Outsider {Guid.NewGuid():N}"[..30]);

        var response = await outsider.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/decline", new { reason = "Not mine to decline" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out of scope is indistinguishable from a code that does not exist");
        await AssertProposalStateAsync(proposalCode, ProposalState.AwardOffered);
    }

    [Fact]
    public async Task An_unresolved_tie_at_the_top_blocks_a_recommendation_until_a_person_resolves_it()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "TieGate");

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
        await seeded.Evaluator.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/submit", null);
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/consolidate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/finalize", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var top = await db.Set<ConsolidatedResult>()
                .FirstAsync(r => r.EvaluationId == seeded.EvaluationId && r.Rank == 1);
            db.Entry(top).Property(nameof(ConsolidatedResult.TieUnresolved)).CurrentValue = true;
            await db.SaveChangesAsync();
        }

        var refused = await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalCode = seeded.ProposalCode,
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await refused.Content.ReadAsStringAsync()).Should().Contain("tie");

        (await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/resolve-tie",
            new { proposalCode = seeded.ProposalCode, reason = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var resolve = await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/resolve-tie",
            new { proposalCode = seeded.ProposalCode, reason = "Prior delivery record on comparable work." });
        resolve.StatusCode.Should().Be(HttpStatusCode.OK, await resolve.Content.ReadAsStringAsync());

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var top = await db.Set<ConsolidatedResult>().AsNoTracking()
                .FirstAsync(r => r.EvaluationId == seeded.EvaluationId && r.Rank == 1);
            top.TieUnresolved.Should().BeFalse();
            top.TieResolutionReason.Should().Be("Prior delivery record on comparable work.");
            top.TieResolvedByUserId.Should().NotBeNull();

            (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                a.Action == "evaluation_tie_resolved" && a.ReferenceCode == seeded.RfqCode))
                .Should().BeTrue("who broke the tie and why is the whole point of surfacing it");
        }

        var accepted = await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalCode = seeded.ProposalCode,
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
    }
}
