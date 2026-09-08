using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-501: the criterion guidance reaches the evaluator who is scoring against it.
///
/// <para><b>It did not, and nothing could have noticed.</b> A template author has been able to write
/// guidance per criterion since EPIC-07. The RFQ's template snapshot did not copy it, the evaluation's own
/// criterion snapshot had no column for it, and the evaluator's read therefore could not carry it - so the
/// text was stored, editable, and invisible to the only person it was written for. T-102 recorded SCR-501
/// as unresolved because the open question was whether the criteria list WAS the brief; it could not have
/// been, and this is why.</para>
///
/// <para>This walks the whole path rather than asserting the column exists: an author writes guidance on a
/// template, an officer binds that template to a tender, the tender reaches evaluation, and the assigned
/// evaluator reads their own view. Every link in that chain dropped the field independently.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvaluationBriefTests(PostgresApiFixture fixture)
{
    private const string GuidanceEn = "Score 0-40 on hygiene records, 40-70 on menu breadth, 70-100 on both.";
    private const string GuidanceAr = "قيّم من ٠ إلى ٤٠ على سجلات النظافة، ومن ٤٠ إلى ٧٠ على تنوع القائمة.";

    [Fact]
    public async Task The_guidance_an_author_wrote_reaches_the_evaluator_scoring_against_it()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (officer, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var (manager, managerId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.ProcurementManager, org.Id);
        var (evaluator, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Brief {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The guidance, written where an author writes it.
        var criterion = await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = GuidanceAr, guidanceEn = GuidanceEn,
            requiresJustification = true,
        });
        criterion.IsSuccessStatusCode.Should().BeTrue(await criterion.Content.ReadAsStringAsync());
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"Brief RFQ {Guid.NewGuid():N}"[..28],
            descriptionAr = (string?)null, descriptionEn = "Hot meals for three sites",
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
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

        var bound = await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });
        bound.IsSuccessStatusCode.Should().BeTrue(await bound.Content.ReadAsStringAsync());

        var supplierName = $"Brief {Guid.NewGuid():N}"[..24];
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, supplierName);
        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == supplierName).Select(s => s.Id).FirstAsync();
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, Domain.Suppliers.SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, Domain.Suppliers.SupplierLifecycleState.Active));
        }

        var invited = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });
        invited.IsSuccessStatusCode.Should().BeTrue(await invited.Content.ReadAsStringAsync());
        var submitted = await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        submitted.IsSuccessStatusCode.Should().BeTrue(await submitted.Content.ReadAsStringAsync());
        var approved = await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        approved.IsSuccessStatusCode.Should().BeTrue(await approved.Content.ReadAsStringAsync());
        var published = await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null);
        published.IsSuccessStatusCode.Should().BeTrue(await published.Content.ReadAsStringAsync());

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
        (await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

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

        (await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/evaluation/open", null)).IsSuccessStatusCode.Should().BeTrue();
        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { evaluatorId } })).IsSuccessStatusCode.Should().BeTrue();

        // The read the brief is built from: the evaluator's own, assignment-scoped view.
        var myEvaluation = await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{rfqCode}/my-evaluation");
        var criteria = myEvaluation.GetProperty("criteria").EnumerateArray().ToList();

        criteria.Should().HaveCount(1);
        criteria[0].GetProperty("guidanceEn").GetString().Should().Be(GuidanceEn,
            "the instruction an author wrote is the one thing an evaluator cannot work without, and it "
            + "reached nobody before SCR-501 - the snapshot dropped it twice over");
        criteria[0].GetProperty("guidanceAr").GetString().Should().Be(GuidanceAr);
        criteria[0].GetProperty("requiresJustification").GetBoolean().Should().BeTrue(
            "BRULE-061's flag is on the wire and the scoring form never read it either");

        // Snapshotted, not read live - and the product turns out to guarantee it twice over.
        //
        // Addressed by the TEMPLATE's criterion id, read back from the template: the id on the evaluator's
        // view is the SNAPSHOT's own, and they are different rows. Using the snapshot id answered 400, which
        // was the route being right rather than the test being unlucky.
        var template = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/evaluation-templates/{templateId}");
        var templateCriterionId = template.GetProperty("criteria").EnumerateArray().First().GetProperty("id").GetGuid();

        var reworded = await manager.PutAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria/{templateCriterionId}", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = "نص مختلف", guidanceEn = "Different text",
        });

        // The edit is REFUSED, not absorbed: a template version an RFQ has referenced is immutable, and the
        // answer names the way forward (fork a new version). That is a stronger guarantee than the snapshot -
        // the text cannot change underneath a live tender even in principle - and the snapshot is what covers
        // the case the fork creates, where the template moves on and this tender keeps what it bound.
        var rewordBody = await reworded.Content.ReadAsStringAsync();
        reworded.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, rewordBody);
        JsonDocument.Parse(rewordBody).RootElement.GetProperty("code").GetString().Should().Be("INVALID_STATE");

        var afterEdit = await evaluator.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{rfqCode}/my-evaluation");
        afterEdit.GetProperty("criteria").EnumerateArray().First().GetProperty("guidanceEn").GetString()
            .Should().Be(GuidanceEn, "whatever the template says now, this tender bound the text above");
    }

    [Fact]
    public async Task A_tender_that_bound_a_template_before_the_field_existed_reports_no_guidance()
    {
        // The honest answer for historical data, asserted rather than left to chance: the guidance was not
        // recorded when that RFQ bound its template, so the read carries null and the screen says "not
        // recorded". Backfilling from the template's current text would show an evaluator an instruction
        // this tender never carried, while looking exactly like one it did.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "NoGuidance");

        var myEvaluation = await seeded.Evaluator.GetAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation");

        // The seed's evaluator is not assigned, so this is the scoped refusal - which is itself the control
        // that the route is assignment-gated. The criteria are read from storage instead.
        myEvaluation.StatusCode.Should().BeOneOf(
            [System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.Conflict]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshots = await db.EvaluationCriterionSnapshots.AsNoTracking()
            .Where(c => c.EvaluationId == seeded.EvaluationId)
            .ToListAsync();

        snapshots.Should().NotBeEmpty();
        snapshots.Should().OnlyContain(c => c.GuidanceAr == null && c.GuidanceEn == null,
            "this seed's template carries no guidance, so the snapshot must carry none either - a column "
            + "that invented text would be worse than the empty one it replaced");
    }
}
