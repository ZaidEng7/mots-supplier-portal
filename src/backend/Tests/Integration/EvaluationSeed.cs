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
    HttpClient Supplier, string ProposalCode,
    // T-028 additions: the buyer-side document routes are keyed by proposal GUID, and the gate is
    // only provable if a document exists on both sides of it. Both are opt-in (see CreateAsync's
    // withDocuments) so the suites that predate T-028 seed exactly what they seeded before.
    Guid ProposalId, Guid TechnicalDocumentId, Guid CommercialDocumentId);

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
    public static async Task<Seeded> CreateAsync(
        PostgresApiFixture fixture, string label, bool withDocuments = false, bool requiresJustification = false)
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
            requiresJustification,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"{label} RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            // A REAL window, not a three-second one.
            //
            // This used to be now+1s to now+3s, and everything between publishing and submitting -
            // approve, publish, a 1.2s wait, the timeline job, starting the proposal, pricing it,
            // setting terms, and with withDocuments TWO file uploads - had to fit inside two seconds. On
            // a loaded machine it did not: the submit was refused by the closed window, the seed did not
            // check the result, and the failure surfaced three steps later as "at least one Submitted
            // proposal is required" from a completely different endpoint.
            //
            // That is the unidentified flake carried in the backlog since batch 9. The window is now an
            // hour, and the seed CLOSES it in storage when it needs it closed - the same technique
            // CrossOrganizationScopeTests adopted after the same class of failure.
            submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddHours(1),
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
        var proposalCode = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposalCode").GetString()!;
        await ProposalPatch.PriceItemAsync(supplier, proposalCode, itemId, 10m, 5m);
        await ProposalPatch.SetTermsAsync(supplier, proposalCode, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
            validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });
        var technicalDocumentId = Guid.Empty;
        var commercialDocumentId = Guid.Empty;
        if (withDocuments)
        {
            // One file the supplier declares Technical, one where the field is simply not sent -
            // the second is the D-7 default, and asserting it is Commercial is the only way to know
            // the default is the gated side rather than whatever the enum happens to declare first.
            technicalDocumentId = await UploadDocumentAsync(supplier, proposalCode, "spec.pdf", "Technical");
            commercialDocumentId = await UploadDocumentAsync(supplier, proposalCode, "prices.pdf", envelope: null);
        }

        // Checked. An unchecked submit here is what made every downstream failure anonymous.
        var submitted = await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());

        // Close the window by moving the deadline into the past, then let the real job notice. No sleep:
        // the job is still what performs the transition, so what is being exercised is unchanged - only
        // the waiting is gone.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == rfqCode)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.SubmissionClosesAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
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

        Guid proposalId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            proposalId = await db.Proposals.Where(p => p.ReferenceCode == proposalCode).Select(p => p.Id).FirstAsync();
        }

        return new Seeded(officer, officerId, manager, managerId, evaluator, evaluatorId,
            rfqCode, supplierUserId, org.Id, evaluationId, criterionCount, SubmittedProposalCount: 1,
            supplier, proposalCode, proposalId, technicalDocumentId, commercialDocumentId);
    }


    /// <summary>Uploads one supporting file to a Draft proposal and returns its id. envelope null
    /// means the multipart field is omitted entirely, not sent empty - "unstated" is the case D-7's
    /// default exists for.</summary>
    private static async Task<Guid> UploadDocumentAsync(
        HttpClient supplier, string proposalCode, string fileName, string? envelope)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(
            "%PDF-1.4\n1 0 obj\n<</Type/Catalog>>\nendobj\ntrailer\n<</Root 1 0 R>>\n%%EOF"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", fileName);
        if (envelope is not null) content.Add(new StringContent(envelope), "envelope");

        var response = await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/documents", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("documents").EnumerateArray()
            .Single(d => d.GetProperty("originalFileName").GetString() == fileName)
            .GetProperty("id").GetGuid();
    }
}

/// <summary>
/// T-068: the evaluator scoring route names a bid by its PUBLIC code, so a test holding a proposal
/// GUID needs the code that addresses it. Resolved from storage rather than threaded through ten
/// setup helpers, which would have meant reshaping every one of their return tuples.
/// </summary>
public static class ProposalCodeLookup
{
    public static async Task<string> ProposalCodeAsync(this PostgresApiFixture fixture, Guid proposalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Proposals.Where(p => p.Id == proposalId).Select(p => p.ReferenceCode).FirstAsync();
    }
}
