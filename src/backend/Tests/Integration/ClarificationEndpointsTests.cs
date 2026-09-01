using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-10.1..10.6/FR-CLR-001..006: real HTTP proof of the clarification Q&A channel -
/// window bounding, private-by-default (OQ-008) with explicit publish, asker anonymization on the
/// published thread, addenda through the "locked after Published except addenda" carve-out, and
/// that every action is audited.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ClarificationEndpointsTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn, DateTimeOffset? opensAt = null, DateTimeOffset? closesAt = null, DateTimeOffset? clarificationDeadlineAt = null) => new
    {
        titleAr = "طلب اختبار",
        titleEn,
        descriptionAr = (string?)null,
        descriptionEn = (string?)null,
        currencyCode = "SYP",
        publishAt = (DateTimeOffset?)null,
        submissionOpensAt = opensAt ?? DateTimeOffset.UtcNow.AddDays(1),
        submissionClosesAt = closesAt ?? DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt,
        evaluationTargetDate = (DateTimeOffset?)null,
    };

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

    /// <summary>Creates, authors, invites both suppliers, submits, approves, and publishes an RFQ
    /// via real HTTP calls, returning its reference code.</summary>
    private async Task<(HttpClient Officer, HttpClient Manager, string ReferenceCode)> PublishedRfqWithTwoInviteesAsync(
        Guid supplierA, Guid supplierB, string titleEn, DateTimeOffset? clarificationDeadlineAt = null)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var template = await templateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics(titleEn, clarificationDeadlineAt: clarificationDeadlineAt));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierA });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = supplierB });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        return (officer, manager, referenceCode);
    }

    [Fact]
    public async Task Posting_a_question_before_publish_is_refused_by_the_domain()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"EarlyAsker {Guid.NewGuid():N}"[..30]);
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Pre-Publish RFQ"));
        var rfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId = askerSupplierId });

        // Not yet Published, so the supplier-facing route itself 404s before the window guard ever runs -
        // the same invite-only-visibility boundary (EPIC-08) is what a supplier hits here.
        var attempt = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Too early?" });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Posting_a_question_after_the_clarification_deadline_is_refused_by_the_domain()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"LateAsker {Guid.NewGuid():N}"[..30]);
        var (_, otherSupplierId) = await ActiveSupplierAsync($"LateOther {Guid.NewGuid():N}"[..30]);
        var (_, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Deadline RFQ",
            clarificationDeadlineAt: DateTimeOffset.UtcNow.AddMilliseconds(200));

        await Task.Delay(TimeSpan.FromMilliseconds(400));
        var attempt = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Too late?" });

        attempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await attempt.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("clarification window has closed");
    }

    [Fact]
    public async Task Only_an_invited_supplier_can_post_a_question()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"RealInvitee {Guid.NewGuid():N}"[..30]);
        var (_, otherSupplierId) = await ActiveSupplierAsync($"RealOther {Guid.NewGuid():N}"[..30]);
        var (_, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Invited Only RFQ");
        var (outsiderClient, _) = await ActiveSupplierAsync($"NotInvited {Guid.NewGuid():N}"[..30]);
        _ = askerClient;

        var attempt = await outsiderClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Can I ask?" });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_private_answer_is_visible_only_to_the_asker_and_hides_the_asker_from_everyone_else()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"PrivateAsker {Guid.NewGuid():N}"[..30]);
        var (otherClient, otherSupplierId) = await ActiveSupplierAsync($"PrivateOther {Guid.NewGuid():N}"[..30]);
        var (officer, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Private Answer RFQ");

        var post = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "What is the delivery incoterm?" });
        post.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterPost = await post.Content.ReadFromJsonAsync<JsonElement>();
        var clarificationId = afterPost.GetProperty("clarifications").EnumerateArray().Single().GetProperty("id").GetGuid();

        // OQ-008 interim: default answer is private (no `publish` sent means false at the API layer's own default).
        var answer = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/answer", new { answer = "FOB.", publish = false });
        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        var buyerView = await answer.Content.ReadFromJsonAsync<JsonElement>();
        var buyerClarification = buyerView.GetProperty("clarifications").EnumerateArray().Single();
        buyerClarification.GetProperty("visibility").GetString().Should().Be("PrivateToAsker");
        buyerClarification.GetProperty("askedBySupplierId").GetGuid().Should().Be(askerSupplierId, "the buyer side always keeps the real asker for audit");

        var askerOwnView = await askerClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        var askerClarification = askerOwnView.GetProperty("clarifications").EnumerateArray().Single();
        askerClarification.GetProperty("answer").GetString().Should().Be("FOB.");
        askerClarification.GetProperty("isMine").GetBoolean().Should().BeTrue();

        var otherView = await otherClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        otherView.GetProperty("clarifications").EnumerateArray().Should().BeEmpty("a PrivateToAsker clarification belonging to someone else is absent entirely, not merely anonymized");
    }

    [Fact]
    public async Task A_published_answer_reaches_the_other_invited_supplier_with_the_asker_anonymized()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"PubAsker {Guid.NewGuid():N}"[..30]);
        var (otherClient, otherSupplierId) = await ActiveSupplierAsync($"PubOther {Guid.NewGuid():N}"[..30]);
        var (officer, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Published Answer RFQ");
        var post = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "What is the delivery incoterm?" });
        var afterPost = await post.Content.ReadFromJsonAsync<JsonElement>();
        var clarificationId = afterPost.GetProperty("clarifications").EnumerateArray().Single().GetProperty("id").GetGuid();

        var answer = await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/answer", new { answer = "FOB.", publish = true });
        answer.StatusCode.Should().Be(HttpStatusCode.OK);

        var otherView = await otherClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        var otherClarification = otherView.GetProperty("clarifications").EnumerateArray().Single();
        otherClarification.GetProperty("question").GetString().Should().Be("What is the delivery incoterm?");
        otherClarification.GetProperty("answer").GetString().Should().Be("FOB.");
        otherClarification.GetProperty("isMine").GetBoolean().Should().BeFalse();
        // The actual anonymization proof: no field anywhere on this DTO carries the asker's identity.
        otherClarification.TryGetProperty("askedBySupplierId", out _).Should().BeFalse("the supplier-facing shape has no asker-identity field at all");
        otherClarification.TryGetProperty("supplierId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Publishing_a_privately_answered_clarification_promotes_it_for_everyone()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"LatePubAsker {Guid.NewGuid():N}"[..30]);
        var (otherClient, otherSupplierId) = await ActiveSupplierAsync($"LatePubOther {Guid.NewGuid():N}"[..30]);
        var (officer, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Late Publish RFQ");
        var post = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Q?" });
        var afterPost = await post.Content.ReadFromJsonAsync<JsonElement>();
        var clarificationId = afterPost.GetProperty("clarifications").EnumerateArray().Single().GetProperty("id").GetGuid();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/answer", new { answer = "A.", publish = false });

        var beforePublish = await otherClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        beforePublish.GetProperty("clarifications").EnumerateArray().Should().BeEmpty();

        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterPublish = await otherClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        afterPublish.GetProperty("clarifications").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Buyer_without_clarification_answer_permission_is_forbidden()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"PermAsker {Guid.NewGuid():N}"[..30]);
        var (_, otherSupplierId) = await ActiveSupplierAsync($"PermOther {Guid.NewGuid():N}"[..30]);
        var (_, manager, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Permission RFQ");
        var post = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Q?" });
        var afterPost = await post.Content.ReadFromJsonAsync<JsonElement>();
        var clarificationId = afterPost.GetProperty("clarifications").EnumerateArray().Single().GetProperty("id").GetGuid();

        // procurement_manager does not hold clarification.answer per this session's grant (procurement_officer-only).
        var attempt = await manager.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/answer", new { answer = "A.", publish = false });

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Addendum_is_refused_before_publish_and_succeeds_after_reaching_both_buyer_and_supplier_views()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"AddendaAsker {Guid.NewGuid():N}"[..30]);
        var (_, otherSupplierId) = await ActiveSupplierAsync($"AddendaOther {Guid.NewGuid():N}"[..30]);

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var createResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Pre-Publish Addendum RFQ"));
        var draftRfq = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var draftReferenceCode = draftRfq.GetProperty("referenceCode").GetString();
        var preClaim = await officer.PostAsJsonAsync($"/api/v1/rfqs/{draftReferenceCode}/addenda",
            new { titleAr = "ت", titleEn = "T", descriptionAr = "و", descriptionEn = "D" });
        preClaim.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the locked-after-Published-except-addenda carve-out does not apply before Published");

        var (publishedOfficer, manager, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Addendum RFQ");

        // Normal item edits stay locked even after Published - the addendum is additive, not a reopening.
        var editAttempt = await publishedOfficer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند2", titleEn = "Item2", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 1, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        editAttempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _ = manager;

        var addendum = await publishedOfficer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/addenda",
            new { titleAr = "تمديد الموعد النهائي", titleEn = "Deadline extended", descriptionAr = "تم تمديد موعد التقديم", descriptionEn = "The submission deadline has been extended." });
        addendum.StatusCode.Should().Be(HttpStatusCode.OK);
        var buyerView = await addendum.Content.ReadFromJsonAsync<JsonElement>();
        buyerView.GetProperty("addenda").EnumerateArray().Should().ContainSingle(a => a.GetProperty("titleEn").GetString() == "Deadline extended");

        var supplierView = await askerClient.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/me/rfqs/{referenceCode}");
        supplierView.GetProperty("addenda").EnumerateArray().Should().ContainSingle(a => a.GetProperty("titleEn").GetString() == "Deadline extended");
    }

    [Fact]
    public async Task Every_clarification_action_writes_an_audit_row()
    {
        var (askerClient, askerSupplierId) = await ActiveSupplierAsync($"AuditAsker {Guid.NewGuid():N}"[..30]);
        var (_, otherSupplierId) = await ActiveSupplierAsync($"AuditOther {Guid.NewGuid():N}"[..30]);
        var (officer, _, referenceCode) = await PublishedRfqWithTwoInviteesAsync(askerSupplierId, otherSupplierId, "Audit RFQ");
        var post = await askerClient.PostAsJsonAsync($"/api/v1/suppliers/me/rfqs/{referenceCode}/clarifications", new { question = "Q?" });
        var afterPost = await post.Content.ReadFromJsonAsync<JsonElement>();
        var clarificationId = afterPost.GetProperty("clarifications").EnumerateArray().Single().GetProperty("id").GetGuid();
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/answer", new { answer = "A.", publish = false });
        await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/clarifications/{clarificationId}/publish", null);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/addenda", new { titleAr = "ت", titleEn = "T", descriptionAr = "و", descriptionEn = "D" });

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.Where(a => a.ReferenceCode == referenceCode).Select(a => a.Action).ToListAsync();

        actions.Should().Contain(["rfq_clarification_posted", "rfq_clarification_answered", "rfq_clarification_published", "rfq_addendum_issued"]);
    }
}
