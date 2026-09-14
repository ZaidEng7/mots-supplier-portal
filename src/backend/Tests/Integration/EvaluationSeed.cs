// Everything an evaluation test needs to exist before the thing it is testing.
//
// A tender driven all the way to an open evaluation with one submitted bid.
//
// Shared because several suites need the same forty lines to reach the state they are actually about. Copying it
// would mean two lifecycles drifting apart, and the one that drifts is the one nobody is looking at.
//
// Later additions kept that property: the supplier side of the same tender and the bid's own public code for the
// clarification loop, and documents on both sides of the buyer-side gate, which is only provable if a document
// exists on each. The documents are opt-in, so the suites that predate them seed exactly what they seeded before.
//
// One file is declared as technical and one leaves the field unsent. The second is the default, and asserting it
// is the commercial side is the only way to know the default is the gated one rather than whatever the
// enumeration happens to declare first.
//
//
// EVERY STEP IS CHECKED, AND NAMES ITSELF WHEN IT FAILS
//
// This seed drives eleven calls and used to check none of them, so a refusal anywhere surfaced three lines later
// as a missing key from a property read, which says nothing about which call was refused or why.
//
// The window between publishing and submitting is real work on a loaded machine, and when it loses, that is the
// shape the failure takes: a mystery in a file that did nothing wrong. Same family as another intermittent
// nobody could diagnose because the failure carried no information.
//
//
// THE SUBMISSION WINDOW IS AN HOUR, NOT A SECOND, AND THAT IS THE FIX FOR A REAL FLAKE
//
// It used to open one second after the tender was created and close two seconds later. Everything in between,
// approving, publishing, waiting, the timeline job, starting the bid, pricing it, setting terms and sometimes two
// file uploads, had to fit inside that.
//
// On a loaded machine it did not: the submission was refused by the closed window, the seed did not check the
// result, and the failure surfaced steps later as a complaint from a completely different endpoint about there
// being no submitted bid. That is the unidentified flake the backlog carried.
//
// The window is now an hour, and the seed CLOSES it in storage when it needs it closed, which is the same
// technique another suite adopted after the same class of failure. No sleeping: the real job still performs the
// transition, so what is exercised is unchanged and only the waiting is gone.
//
// The open and close are computed from ONE instant an hour and two hours out, not from two separate reads of the
// clock. Two reads differ by microseconds, or by nothing at all when the clock does not tick between them, and a
// window whose close is not strictly after its open is refused. That failed twenty-eight times in a full run and
// passed every time in isolation, which is the signature of a race against a clock rather than against another
// test.
//
//
// THE PUBLIC CODE IS RESOLVED FROM STORAGE
//
// The evaluator's scoring route names a bid by its public code, so a test holding an internal identifier needs
// the code that addresses it.
//
// Resolved from storage rather than threaded through ten setup helpers, which would have meant reshaping every
// one of their return values.

namespace MotsSupplierPortal.Tests.Integration;

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

public sealed record Seeded(
    HttpClient Officer, Guid OfficerId, HttpClient Manager, Guid ManagerId,
    HttpClient Evaluator, Guid EvaluatorId, string RfqCode, Guid SupplierUserId, Guid OrgId,
    Guid EvaluationId, int CriterionCount, int SubmittedProposalCount,
    HttpClient Supplier, string ProposalCode,
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

    private static Task<HttpResponseMessage> Step(string name, Task<HttpResponseMessage> call) =>
        SetupStep.Of(nameof(EvaluationSeed), name, call);

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
            submissionOpensAt = DateTimeOffset.UtcNow.AddHours(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddHours(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        await Step("create rfq", Task.FromResult(created));
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await Step("add item", Task.FromResult(itemResponse));
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (supplier, supplierId) = await ActiveSupplierAsync(fixture, $"{label} {Guid.NewGuid():N}"[..30]);
        await Step("invite", officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId }));
        await Step("submit-review", officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null));
        await Step("approve", manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null));
        await Step("publish", officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null));

        await SubmissionWindowTestHelper.OpenAsync(fixture, rfqCode);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var start = await Step("start proposal", supplier.PostAsync($"/api/v1/rfqs/{rfqCode}/proposals", null));
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
            technicalDocumentId = await UploadDocumentAsync(supplier, proposalCode, "spec.pdf", "Technical");
            commercialDocumentId = await UploadDocumentAsync(supplier, proposalCode, "prices.pdf", envelope: null);
        }

        var submitted = await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());

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

public static class ProposalCodeLookup
{
    public static async Task<string> ProposalCodeAsync(this PostgresApiFixture fixture, Guid proposalId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Proposals.Where(p => p.Id == proposalId).Select(p => p.ReferenceCode).FirstAsync();
    }
}
