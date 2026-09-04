using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-401's two queues: "RFQ publish approvals + award approvals".
///
/// <para><b>The defect this screen can reproduce is PR #90's.</b> A Procurement Manager holds
/// rfq.read, rfq.review, rfq.approve and rfq.cancel - but NOT rfq.create. A queue that lists work
/// the manager cannot then open is that bug in a new place, so the tests follow the link the row
/// offers rather than asserting the row rendered.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ApprovalQueuesTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> QueuesAsync(HttpClient manager)
    {
        var response = await manager.GetAsync("/api/v1/procurement/approvals");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Everything_the_RFQ_queue_lists_the_manager_can_actually_open()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "QueueReach");

        // A second RFQ, left in InternalReview so it sits in the publish-approval queue.
        var created = await seeded.Officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"Queue Reach {Guid.NewGuid():N}"[..24],
            descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await seeded.Officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await seeded.Officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations",
            new { supplierId = await SupplierIdOfAsync(seeded) });
        await seeded.Officer.PutAsJsonAsync($"/api/v1/rfqs/{code}/evaluation-template",
            new { evaluationTemplateId = await TemplateIdOfAsync(seeded) });
        await seeded.Officer.PostAsync($"/api/v1/rfqs/{code}/submit-review", null);

        var queues = await QueuesAsync(seeded.Manager);
        var rows = queues.GetProperty("rfqPublishApprovals").EnumerateArray().ToList();

        rows.Should().NotBeEmpty("control: the RFQ really is waiting for approval");

        // The assertion that matters: FOLLOW the link, do not trust the row.
        foreach (var row in rows)
        {
            var href = row.GetProperty("href").GetString()!;
            var open = await seeded.Manager.GetAsync(href);

            open.StatusCode.Should().Be(HttpStatusCode.OK,
                $"a manager must be able to open everything the queue offers them - {href}");
        }
    }

    [Fact]
    public async Task An_award_the_manager_recommended_themselves_is_not_offered_to_them()
    {
        // EPIC-14's segregation of duties, applied to the QUEUE rather than only to the write. An
        // award listed here that the manager will be refused on click is the same shape of defect as
        // a row they cannot open.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "QueueSod");
        await ScoreAndConsolidateAsync(seeded);

        // The manager recommends, then routes for approval - so the pending award is their own.
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/recommend", new
        {
            winningProposalId = await WinningProposalIdAsync(seeded),
            justificationAr = "الأفضل", justificationEn = "Best",
        });
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/award/route-for-approval", null);

        var ownQueue = await QueuesAsync(seeded.Manager);
        ownQueue.GetProperty("awardApprovals").EnumerateArray()
            .Select(a => a.GetProperty("rfqReferenceCode").GetString())
            .Should().NotContain(seeded.RfqCode,
                "an approver may not approve their own recommendation, so it must not be offered");

        // The control, and the proof the award really is pending: a DIFFERENT manager in the same
        // organization does see it, and can open it.
        var otherManager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, seeded.OrgId);
        var otherQueue = await QueuesAsync(otherManager);
        var row = otherQueue.GetProperty("awardApprovals").EnumerateArray()
            .Single(a => a.GetProperty("rfqReferenceCode").GetString() == seeded.RfqCode);

        var open = await otherManager.GetAsync(row.GetProperty("href").GetString()!);
        open.StatusCode.Should().Be(HttpStatusCode.OK, "the queue lists only work its reader can reach");
    }

    [Fact]
    public async Task The_queues_are_this_organizations_and_never_another_organizations()
    {
        var mine = await EvaluationSeed.CreateAsync(fixture, "QueueMine");
        var theirs = await EvaluationSeed.CreateAsync(fixture, "QueueTheirs");

        await theirs.Officer.PostAsync($"/api/v1/rfqs/{theirs.RfqCode}/submit-review", null);

        var myQueues = await QueuesAsync(mine.Manager);

        myQueues.GetProperty("rfqPublishApprovals").EnumerateArray()
            .Select(r => r.GetProperty("rfqReferenceCode").GetString())
            .Should().NotContain(theirs.RfqCode, "another organization's approvals are not this manager's work");
    }

    [Fact]
    public async Task An_officer_cannot_read_the_approval_queues()
    {
        // SCREEN-INVENTORY gives SCR-401 to procurement_manager, and its "denied" state is one of the
        // six it lists. An officer holds rfq.create but not rfq.approve - the exact inverse of the
        // manager, which is why the permission and not the role is what gates this.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "QueueDenied");

        var response = await seeded.Officer.GetAsync("/api/v1/procurement/approvals");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- setup helpers --------------------------------------------------------------------------

    private async Task<Guid> SupplierIdOfAsync(Seeded seeded)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Id == seeded.SupplierUserId).Select(u => u.SupplierId!.Value).FirstAsync();
    }

    private async Task<Guid> TemplateIdOfAsync(Seeded seeded)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EvaluationTemplates.Where(t => t.Status == EvaluationTemplateStatus.Active)
            .OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync();
    }

    private async Task<Guid> WinningProposalIdAsync(Seeded seeded)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Proposals
            .Where(p => db.Rfqs.Any(r => r.Id == p.RfqId && r.ReferenceCode == seeded.RfqCode))
            .Select(p => p.Id).FirstAsync();
    }

    private async Task ScoreAndConsolidateAsync(Seeded seeded)
    {
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        Guid proposalId = await WinningProposalIdAsync(seeded);
        List<Guid> criterionIds;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            criterionIds = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == seeded.EvaluationId).Select(c => c.Id).ToListAsync();
        }

        foreach (var criterionId in criterionIds)
        {
            await seeded.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/scores",
                new { proposalCode = await fixture.ProposalCodeAsync(proposalId), criterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });
        }

        await seeded.Evaluator.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/submit", null);
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/consolidate", null);
        await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/finalize", null);
    }
}
