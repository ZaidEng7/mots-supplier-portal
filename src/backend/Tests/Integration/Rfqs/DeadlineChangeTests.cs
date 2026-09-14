// Moving a submission deadline: who may move it in which direction, and what the record says afterwards.
//
// The written rule gives extension to the officer and shortening to the manager, while the tender is published or
// open, with an audit action and a notification to every invitee.
//
//
// THE REFUSAL RUNS BOTH WAYS, OR IT IS ONLY HALF A RULE
//
// The officer reaches the handler holding the edit permission and is refused on DIRECTION. The manager, on the
// same route with a shortening payload, is accepted.
//
// And the mirror: a manager may not EXTEND.
//
//
// A PAST DATE IS REFUSED, AND THAT IS COHERENCE RATHER THAN POLICY
//
// A past deadline would close the tender on the timeline job's next run, making a "shortening" an immediate close
// by side effect and skipping the close operation's own rules.
//
// It is attempted as the manager, because a past date is a shortening and that is the manager's direction, and the
// control is a future shortening from the same caller landing, so the refusal is about the date rather than about
// the caller or the direction.
//
//
// THE STATE GUARD, AND WHY THERE IS NO THIRD CASE
//
// The rule permits a change only while the tender is published or open. That is reproduced through the real
// timeline job rather than by writing a state into the row, and the job is run twice, because it makes one
// transition per run: the first opens the window and the second closes it. One run left it open, which is how this
// was found.
//
// On a closed tender the officer gets the STATE refusal, which is the guard under test, while the manager is
// refused earlier and for a different reason, because permission is checked before the aggregate is touched.
// That difference is asserted rather than smoothed over, because a reader comparing the two responses should know
// why they differ.
//
// There is no third case: the deadline is already in the past, so every date the domain would accept is later and
// therefore an extension, and a manager can only ever meet the permission refusal there.
//
//
// TWO AUDIT ACTIONS, NOT ONE
//
// Shortening gets its own, rather than "extended" with a smaller number in it: an audit search for who cut a
// tender short must not have to read two timestamps out of a row named extended.
//
// The extension row carries BOTH dates, because a recorded decision leaves an extension uncapped and calls this
// row the only control on an abusive one.
//
// And the reason is required, which is what makes every change defensible or obviously indefensible. Without it
// that row records only that somebody moved a date. It is asserted refused with no reason, which is the half that
// makes the rest meaningful, and asserted present in the audit row rather than merely the dates.
//
//
// WHAT THE NOTIFICATION CARRIES, AND WHERE THE REASON IS READ
//
// Every invitee is told, and the payload carries no date, because the allow-list treats a date as content.
//
// That is asserted on the DATA map specifically, which is what the allow-list governs. The date does appear in the
// de-duplication key, deliberately: two successive changes are two pieces of news, and a key on the tender alone
// would swallow the second. A de-duplication key is routing plumbing that is never rendered to anyone; the
// allow-list's concern is what reaches a reader.
//
// The supplier reads the reason on the tender, where the deadline itself is. Not in the payload: the allow-list is
// identifiers and public codes, and it already refused a date on the grounds that a date is content, so a
// free-text reason cannot go there either. The notification points here.
//
// A shortening notification is an ADDITION beyond the written rule, which names one only for an extension. A
// window closing earlier is what a bidder must hear about most urgently.
//
// The assertions read storage rather than the response body, and the setup binds a scoring template because
// publication refuses a tender without one.

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;
using Xunit;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DeadlineChangeTests(PostgresApiFixture fixture)
{
    private async Task<(string RfqCode, HttpClient Officer, HttpClient Manager, Guid SupplierUserId)>
        PublishedRfqAsync(string label)
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, label);
        return (seeded.RfqCode, seeded.Officer, seeded.Manager, seeded.SupplierUserId);
    }

    private async Task<DateTimeOffset?> DeadlineAsync(string rfqCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Rfqs.AsNoTracking().Where(r => r.ReferenceCode == rfqCode)
            .Select(r => r.SubmissionClosesAt).FirstAsync();
    }

    private async Task<(string RfqCode, HttpClient Officer, HttpClient Manager, Guid OrgId, HttpClient Supplier)> OpenRfqAsync(string label)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"{label} RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddMinutes(5),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(7),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });

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

        var (supplier, supplierId) = await ActiveSupplierAsync($"{label} {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (rfqCode, officer, manager, org.Id, supplier);
    }

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, Domain.Suppliers.SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, Domain.Suppliers.SupplierLifecycleState.Active));
        return (client, supplier.Id);
    }

    [Fact]
    public async Task An_officer_extends_the_deadline_and_every_invitee_is_told()
    {
        var (rfqCode, officer, _, _, _) = await OpenRfqAsync("Extend");
        var before = await DeadlineAsync(rfqCode);
        var extended = before!.Value.AddDays(7);

        var response = await officer.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = extended, reason = "Supplier request for more preparation time." });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await DeadlineAsync(rfqCode)).Should().BeCloseTo(extended, TimeSpan.FromSeconds(1));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.ReferenceCode == rfqCode && a.Action == "rfq.deadline_extended");
        audit.FromState.Should().NotBeNullOrEmpty("an extension without the previous date says nothing about by how much");
        audit.ToState.Should().NotBeNullOrEmpty();

        var payloads = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
        var deadlineMessages = payloads.Where(p => p.Contains("rfq.deadline_extended")).ToList();
        deadlineMessages.Should().NotBeEmpty();

        foreach (var message in deadlineMessages)
        {
            var data = JsonDocument.Parse(message).RootElement.GetProperty("data");
            data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["rfqCode"],
                "BRULE-091's allow-list treats a date as content, so the copy points at the RFQ instead");
        }
    }

    [Fact]
    public async Task An_officer_cannot_shorten_the_window_and_a_manager_can()
    {
        var (rfqCode, officer, manager, _, _) = await OpenRfqAsync("Shorten");
        var before = await DeadlineAsync(rfqCode);
        var shortened = before!.Value.AddDays(-3);

        var refused = await officer.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = shortened, reason = "Supplier request for more preparation time." });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "403 not 404: the caller demonstrably can see this RFQ, so hiding its existence protects nothing");
        (await DeadlineAsync(rfqCode)).Should().BeCloseTo(before.Value, TimeSpan.FromSeconds(1),
            "a refused shortening leaves the aggregate untouched");

        (await manager.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = before.Value.AddDays(3), reason = "Supplier request for more preparation time." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "extension is the officer's direction");

        var allowed = await manager.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = shortened, reason = "Supplier request for more preparation time." });

        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await DeadlineAsync(rfqCode)).Should().BeCloseTo(shortened, TimeSpan.FromSeconds(1));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == rfqCode && a.Action == "rfq.deadline_shortened"))
            .Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == rfqCode && a.Action == "rfq.deadline_extended"))
            .Should().BeFalse("no extension happened here");

        var payloads = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
        payloads.Should().Contain(p => p.Contains("rfq.deadline_shortened"));
    }

    [Fact]
    public async Task A_deadline_in_the_past_is_refused_even_from_a_manager()
    {
        var (rfqCode, _, manager, _, _) = await OpenRfqAsync("PastDate");
        var before = await DeadlineAsync(rfqCode);

        var response = await manager.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = DateTimeOffset.UtcNow.AddDays(-1), reason = "Supplier request for more preparation time." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DeadlineAsync(rfqCode)).Should().BeCloseTo(before!.Value, TimeSpan.FromSeconds(1));

        (await manager.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = before.Value.AddDays(-1), reason = "Supplier request for more preparation time." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_deadline_cannot_be_changed_once_submissions_have_closed()
    {
        var (rfqCode, officer, manager, _, _) = await OpenRfqAsync("Closed");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == rfqCode).ExecuteUpdateAsync(p => p
                .SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddSeconds(-3))
                .SetProperty(r => r.SubmissionClosesAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        for (var i = 0; i < 2; i++)
        {
            await using var jobScope = fixture.Services.CreateAsyncScope();
            await jobScope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.Rfqs.AsNoTracking().Where(r => r.ReferenceCode == rfqCode)
                .Select(r => r.State).FirstAsync();
            state.Should().Be(RfqState.SubmissionClosed, "the job closed it, so the guard below is about state");
        }

        var later = DateTimeOffset.UtcNow.AddDays(3);
        (await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = later, reason = "Supplier request for more preparation time." }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "reopening a closed window is not an extension - it would resurrect a tender after bidding ended");

        (await manager.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = later, reason = "Supplier request for more preparation time." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    }

    [Fact]
    public async Task Another_organizations_officer_cannot_touch_the_deadline()
    {
        var (rfqCode, _, _, _, _) = await OpenRfqAsync("Scope");
        var before = await DeadlineAsync(rfqCode);

        var outsiderOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, outsiderOrg.Id);

        var response = await outsider.PostAsJsonAsync(
            $"/api/v1/rfqs/{rfqCode}/deadline", new { submissionDeadline = before!.Value.AddDays(7), reason = "Supplier request for more preparation time." });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: another organization's RFQ is indistinguishable from one that does not exist");
        (await DeadlineAsync(rfqCode)).Should().BeCloseTo(before.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_deadline_change_needs_a_reason_and_the_reason_reaches_the_supplier()
    {
        var (rfqCode, officer, _, _, supplier) = await OpenRfqAsync("Reasoned");
        var before = await DeadlineAsync(rfqCode);

        (await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/deadline",
            new { submissionDeadline = before!.Value.AddDays(3), reason = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var accepted = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/deadline",
            new { submissionDeadline = before.Value.AddDays(3), reason = "The Ministry extended the tender period." });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.AuditLogs.AsNoTracking()
                .FirstAsync(a => a.ReferenceCode == rfqCode && a.Action == "rfq.deadline_extended");
            row.Reason.Should().Be("The Ministry extended the tender period.");
        }

        var supplierView = await supplier.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{rfqCode}");
        supplierView.GetProperty("submissionDeadlineChangeReason").GetString()
            .Should().Be("The Ministry extended the tender period.");
        supplierView.GetProperty("submissionDeadlineChangedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
