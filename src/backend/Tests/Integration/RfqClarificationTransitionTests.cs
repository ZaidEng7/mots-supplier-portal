using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T3-36 through the real HTTP surface: the three states BUSINESS-PROCESSES.md §3.1 defines and no
/// code path could reach, plus §3's 409 that reports where an RFQ can actually go.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqClarificationTransitionTests(PostgresApiFixture fixture)
{
    private sealed record Setup(
        HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
        HttpClient Evaluator, Guid EvaluatorId, string RfqCode, Guid SupplierUserId, Guid OrgId);

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    /// <summary>An RFQ in UnderEvaluation - the state all three new transitions hang off.</summary>
    private async Task<Setup> UnderEvaluationAsync(string label)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, officerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, managerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Tpl {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"{label} RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddSeconds(3),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (supplier, supplierId) = await ActiveSupplierAsync($"{label} {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null);

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var start = await supplier.PostAsync($"/api/v1/rfqs/{rfqCode}/proposals", null);
        var proposalCode = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposalCode").GetString()!;
        await ProposalPatch.PriceItemAsync(supplier, proposalCode, itemId, 10m, 5m);
        await ProposalPatch.SetTermsAsync(supplier, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
            validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);

        await Task.Delay(TimeSpan.FromSeconds(2.2));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var opened = await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/evaluation/open", null);
        opened.StatusCode.Should().Be(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());

        Guid supplierUserId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierUserId = await db.Users.Where(u => u.SupplierId == supplierId).Select(u => u.Id).FirstAsync();
        }

        return new Setup(officer, officerId, manager, managerId, evaluator, evaluatorId, rfqCode, supplierUserId, org.Id);
    }

    private async Task<RfqState> StateOfAsync(string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Rfqs.Where(r => r.ReferenceCode == rfqCode).Select(r => r.State).FirstAsync();
    }

    // ---- the transitions ------------------------------------------------------------------------

    [Fact]
    public async Task Requesting_and_resolving_a_clarification_moves_the_RFQ_and_notifies_both_sides()
    {
        var setup = await UnderEvaluationAsync("Clar");

        var request = await setup.Officer.PostAsJsonAsync(
            $"/api/v1/rfqs/{setup.RfqCode}/request-clarification", new { reason = "Missing delivery schedule" });
        request.StatusCode.Should().Be(HttpStatusCode.OK, await request.Content.ReadAsStringAsync());
        (await StateOfAsync(setup.RfqCode)).Should().Be(RfqState.Clarification);

        // §3.1's notification column: "Email + in-app to targeted supplier".
        var supplierRows = await NotificationTestHelper.ForRecipientAsync(
            fixture, setup.SupplierUserId, NotificationTypes.RfqClarificationRequested);
        supplierRows.Should().ContainSingle();

        var resolve = await setup.Officer.PostAsync($"/api/v1/rfqs/{setup.RfqCode}/resolve-clarification", null);
        resolve.StatusCode.Should().Be(HttpStatusCode.OK, await resolve.Content.ReadAsStringAsync());
        (await StateOfAsync(setup.RfqCode)).Should().Be(RfqState.UnderEvaluation);

        // §3.1: "In-app to committee".
        var committeeRows = await NotificationTestHelper.ForRecipientAsync(
            fixture, setup.OfficerId, NotificationTypes.RfqClarificationResolved);
        committeeRows.Should().ContainSingle();
    }

    [Fact]
    public async Task An_evaluator_can_request_a_clarification_because_the_table_names_them_an_actor()
    {
        // §3.1: "Request clarification | `procurement_officer`,`evaluator` / `rfq.clarify`". The
        // evaluator half is easy to drop when wiring a permission, and this is what catches it.
        var setup = await UnderEvaluationAsync("ClarEval");

        var response = await setup.Evaluator.PostAsJsonAsync(
            $"/api/v1/rfqs/{setup.RfqCode}/request-clarification", new { reason = "Specification unclear" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_persona_without_rfq_clarify_is_refused()
    {
        // The negative, with the two tests above as its control: the route works, and it works for
        // exactly the personas §3.1 names.
        var setup = await UnderEvaluationAsync("ClarDenied");
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, setup.OrgId);

        var response = await reviewer.PostAsJsonAsync(
            $"/api/v1/rfqs/{setup.RfqCode}/request-clarification", new { reason = "Not mine to ask" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StateOfAsync(setup.RfqCode)).Should().Be(RfqState.UnderEvaluation, "a refused call must not move the RFQ");
    }

    [Fact]
    public async Task Consolidating_the_evaluation_moves_the_RFQ_into_Shortlisting()
    {
        var setup = await UnderEvaluationAsync("Short");

        await setup.Manager.PostAsJsonAsync($"/api/v1/rfqs/{setup.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { setup.EvaluatorId } });

        Guid proposalId;
        List<Guid> criterionIds;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluationId = await db.Evaluations
                .Where(e => db.Rfqs.Any(r => r.Id == e.RfqId && r.ReferenceCode == setup.RfqCode))
                .Select(e => e.Id).FirstAsync();
            criterionIds = await db.EvaluationCriterionSnapshots
                .Where(c => c.EvaluationId == evaluationId).Select(c => c.Id).ToListAsync();
            proposalId = await db.Proposals
                .Where(p => db.Rfqs.Any(r => r.Id == p.RfqId && r.ReferenceCode == setup.RfqCode))
                .Select(p => p.Id).FirstAsync();
        }

        foreach (var criterionId in criterionIds)
        {
            await setup.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{setup.RfqCode}/my-evaluation/scores",
                new { proposalCode = await fixture.ProposalCodeAsync(proposalId), criterionId, rawScore = 80m, commentAr = (string?)null, commentEn = (string?)null });
        }
        await setup.Evaluator.PostAsync($"/api/v1/rfqs/{setup.RfqCode}/my-evaluation/submit", null);

        var consolidate = await setup.Manager.PostAsync($"/api/v1/rfqs/{setup.RfqCode}/evaluation/consolidate", null);
        consolidate.StatusCode.Should().Be(HttpStatusCode.OK, await consolidate.Content.ReadAsStringAsync());

        // §3.1: "UnderEvaluation | Shortlisting | Begin shortlisting | … / `evaluation.consolidate`".
        (await StateOfAsync(setup.RfqCode)).Should().Be(RfqState.Shortlisting);

        var rows = await NotificationTestHelper.ForRecipientAsync(
            fixture, setup.OfficerId, NotificationTypes.RfqShortlistingStarted);
        rows.Should().ContainSingle();
    }

    // ---- §3's 409 -------------------------------------------------------------------------------

    [Fact]
    public async Task An_illegal_transition_answers_409_with_the_current_state_and_where_it_can_go()
    {
        var setup = await UnderEvaluationAsync("Illegal");

        // Resolving a clarification that was never requested. Legal from Clarification, not from
        // UnderEvaluation - which is exactly the shape §3's response exists to explain.
        var response = await setup.Officer.PostAsync($"/api/v1/rfqs/{setup.RfqCode}/resolve-clarification", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("code").GetString().Should().Be("ILLEGAL_TRANSITION");
        problem.GetProperty("type").GetString().Should().EndWith("/errors/invalid-state-transition");
        problem.GetProperty("currentState").GetString().Should().Be(nameof(RfqState.UnderEvaluation));

        var allowed = problem.GetProperty("allowedNext").EnumerateArray().Select(n => n.GetString()).ToList();
        allowed.Should().BeEquivalentTo(new[]
        {
            nameof(RfqState.Clarification), nameof(RfqState.Shortlisting),
            nameof(RfqState.AwardApproval), nameof(RfqState.Cancelled),
        }, "§12.4: the 409 \"includes allowedNext\", and T3-36 changed what follows UnderEvaluation");
    }

    [Fact]
    public async Task A_refusal_that_is_not_a_transition_problem_stays_a_400()
    {
        // The other direction of the same gate. Requesting a clarification IS legal from
        // UnderEvaluation, so a refusal here is about the request - the missing reason - and must not
        // be dressed up as a state-machine conflict, or a client would refetch and retry forever.
        var setup = await UnderEvaluationAsync("NotIllegal");

        var response = await setup.Officer.PostAsJsonAsync(
            $"/api/v1/rfqs/{setup.RfqCode}/request-clarification", new { reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an empty reason fails validation before the aggregate is touched");
    }
}
