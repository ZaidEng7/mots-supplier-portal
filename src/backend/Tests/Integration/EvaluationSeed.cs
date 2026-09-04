using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>Everything an evaluation test needs to exist before the thing it is testing.</summary>
public sealed record Seeded(
    HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
    HttpClient Evaluator, Guid EvaluatorId, string RfqCode, Guid SupplierUserId, Guid OrgId,
    Guid EvaluationId, int CriterionCount, int SubmittedProposalCount,
    // T-051 additions: the clarification loop needs the SUPPLIER side of this same RFQ, and the
    // proposal's own code. Added here rather than re-seeding forty lines in a third suite - the
    // reason this helper exists at all.
    HttpClient Supplier, string ProposalCode);

public static class EvaluationSeed
{
    private static async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(
        PostgresApiFixture fixture, string name)
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

    /// <summary>
    /// An RFQ driven all the way to an open evaluation with one submitted proposal.
    ///
    /// <para>Shared because two suites need the same forty lines to reach the state they are actually
    /// about - T3-36's transitions and SCR-500's assignments both begin at UnderEvaluation. Copying
    /// it would mean two lifecycles drifting apart, and the one that drifts is the one nobody is
    /// looking at.</para>
    /// </summary>
    public static async Task<Seeded> CreateAsync(PostgresApiFixture fixture, string label)
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

        var (supplier, supplierId) = await ActiveSupplierAsync(fixture, $"{label} {Guid.NewGuid():N}"[..30]);
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
        var proposalCode = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;
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

        int criterionCount;
        Guid evaluationId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            evaluationId = await db.Evaluations
                .Where(e => db.Rfqs.Any(r => r.Id == e.RfqId && r.ReferenceCode == rfqCode))
                .Select(e => e.Id).FirstAsync();
            criterionCount = await db.EvaluationCriterionSnapshots.CountAsync(c => c.EvaluationId == evaluationId);
        }

        return new Seeded(officer, officerId, manager, managerId, evaluator, evaluatorId,
            rfqCode, supplierUserId, org.Id, evaluationId, criterionCount, SubmittedProposalCount: 1,
            supplier, proposalCode);
    }

}
