using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-031: an aggregate records when it entered its current state, and the approval queue can
/// therefore say how long a tender has been waiting.
///
/// <para><b>What was missing.</b> Nothing stored the instant, so the queue returned a null
/// waiting-since with a comment explaining that every available date was wrong: <c>CreatedAt</c> would
/// read as "waiting three weeks" for a tender drafted three weeks ago and submitted yesterday, and the
/// approval row's own timestamp records the end of a wait rather than its start.</para>
///
/// <para><b>What these assert.</b> That a real transition stamps it, that an edit which is not a
/// transition does not, and that the queue shows the stamped value. The middle one is the assertion
/// that matters: a column that moved on every write would make every queue row look like a fresh
/// arrival, which is worse than the null it replaces.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StateChangedAtTests(PostgresApiFixture fixture)
{
    private async Task<DateTimeOffset?> StateChangedAtAsync(string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqCode)
            .Select(r => r.StateChangedAt)
            .FirstAsync();
    }

    private async Task<(HttpClient Officer, HttpClient Manager, string RfqCode, Guid OrgId)> DraftRfqAsync(string title)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = title, descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddHours(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddHours(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });

        // Submitting for review needs a bound evaluation template (the gate says so by name), so the
        // draft is given one here rather than in the one test that transitions - every test in this
        // file wants a tender that COULD move.
        var template = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Tpl {Guid.NewGuid():N}" });
        var templateId = (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        // And an invited supplier, the gate's other stated requirement.
        var (_, supplierId) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Stamp {Guid.NewGuid():N}"[..30]);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync(x => x.DisplayNameEn.StartsWith("Stamp "));
            await db.Suppliers.Where(x => x.Id == supplier.Id).ExecuteUpdateAsync(p => p
                .SetProperty(x => x.OnboardingState, Domain.Suppliers.SupplierOnboardingState.Approved)
                .SetProperty(x => x.LifecycleState, Domain.Suppliers.SupplierLifecycleState.Active));
            await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId = supplier.Id });
        }

        return (officer, manager, rfqCode, org.Id);
    }

    [Fact]
    public async Task A_new_tender_is_stamped_when_it_enters_its_first_state()
    {
        var (_, _, rfqCode, _) = await DraftRfqAsync($"Stamp {Guid.NewGuid():N}"[..20]);

        // Not null. A row created after the column exists has entered a state, and null is reserved
        // for rows that predate the column and genuinely have no recorded instant.
        (await StateChangedAtAsync(rfqCode)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_edit_that_is_not_a_transition_leaves_the_stamp_alone()
    {
        // The assertion the whole field depends on. A stamp that moved on every write would make a
        // three-week-old queue row read as a fresh arrival - worse than the null it replaces, because
        // it is confidently wrong.
        var (officer, _, rfqCode, _) = await DraftRfqAsync($"NoMove {Guid.NewGuid():N}"[..20]);
        var before = await StateChangedAtAsync(rfqCode);

        // The full-replacement PUT §12-A gives this route, with the same window the draft was created
        // with - the edit under test is the TITLE, and a changed window would be a different question.
        var edited = await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}", new
        {
            titleAr = "طلب", titleEn = "Edited title", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddHours(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddHours(2),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        (await StateChangedAtAsync(rfqCode)).Should().Be(before);
    }

    [Fact]
    public async Task A_transition_moves_the_stamp_and_the_approval_queue_shows_it()
    {
        var (officer, manager, rfqCode, _) = await DraftRfqAsync($"Moves {Guid.NewGuid():N}"[..20]);
        var atDraft = await StateChangedAtAsync(rfqCode);

        var submitted = await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());

        var atReview = await StateChangedAtAsync(rfqCode);
        atReview.Should().NotBeNull();
        atReview.Should().BeAfter(atDraft!.Value, "entering review is a state change");

        // And the queue reads it. This row returned null before T-031, with a comment saying no
        // honest source existed - so the assertion is that the screen a manager works from can now
        // say how long the tender has been waiting on them.
        var queuesResponse = await manager.GetAsync("/api/v1/procurement/approvals");
        queuesResponse.StatusCode.Should().Be(HttpStatusCode.OK, await queuesResponse.Content.ReadAsStringAsync());
        var queues = await queuesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var row = queues.GetProperty("rfqPublishApprovals").EnumerateArray()
            .Single(r => r.GetProperty("rfqReferenceCode").GetString() == rfqCode);

        row.GetProperty("waitingSince").GetDateTimeOffset().Should().Be(atReview!.Value);
    }
}
