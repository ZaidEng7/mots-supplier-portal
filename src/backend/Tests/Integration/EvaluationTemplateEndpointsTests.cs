using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>FEAT-11.1/FR-ADM-005, pulled forward for EPIC-07: the real endpoint-level proof for
/// the weight-sum and immutability/versioning invariants already unit-tested on the aggregate
/// directly (EvaluationTemplateTests.cs) - here through the real HTTP surface, permission-guarded,
/// audited.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvaluationTemplateEndpointsTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> ManagerClientAsync() => StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager);

    private static async Task<JsonElement> CreateTemplateAsync(HttpClient client, string nameEn = "Standard Template")
    {
        var response = await client.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// The read is wider than the writes, and this test changed in batch 12 to say so.
    ///
    /// <para>It used to assert that a procurement officer could not LIST templates. That was the
    /// behaviour, and it made the product unusable: binding a template to a tender is gated on
    /// rfq.edit, which an officer holds, and binding is a precondition of submitting for review - so
    /// the officer could bind a template the API forbade them to see, the picker came back empty, and
    /// no tender could ever leave Draft. Found by walking a procurement from an empty database.</para>
    ///
    /// <para>The write half is unchanged and is asserted here as the control, because widening a read
    /// is only defensible if the writes stay where they were.</para>
    /// </summary>
    [Fact]
    public async Task An_officer_may_read_templates_but_not_change_them()
    {
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var list = await officer.GetAsync("/api/v1/evaluation-templates");
        list.StatusCode.Should().Be(HttpStatusCode.OK, "binding a template requires seeing the list of them");

        var create = await officer.PostAsJsonAsync("/api/v1/evaluation-templates", new { nameAr = "قالب", nameEn = "Officer attempt" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, "authoring a scoring scheme is the manager's");
    }

    [Fact]
    public async Task A_supplier_may_not_read_templates()
    {
        // The other control. Widening the read to rfq.edit must not widen it to everyone signed in -
        // and it nearly did: moving the permission off the endpoint GROUP left the by-id read with
        // nothing but RequireAuthorization until the generated permission catalogue caught it.
        var (supplier, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, $"Tpl {Guid.NewGuid():N}"[..20]);

        var response = await supplier.GetAsync("/api/v1/evaluation-templates");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Activation_is_rejected_when_criterion_weights_do_not_sum_to_100()
    {
        var manager = await ManagerClientAsync();
        var template = await CreateTemplateAsync(manager, "Weight Test Template");
        var templateId = template.GetProperty("id").GetGuid();

        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Criterion A", dimension = "Technical", weight = 40, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار ب", nameEn = "Criterion B", dimension = "Commercial", weight = 30, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });

        var activate = await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await activate.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_STATE");
        body.GetProperty("detail").GetString().Should().Contain("sum to exactly 100");
    }

    [Fact]
    public async Task Activation_succeeds_when_weights_sum_to_100_and_the_template_becomes_usable()
    {
        var manager = await ManagerClientAsync();
        var template = await CreateTemplateAsync(manager, "Activatable Template");
        var templateId = template.GetProperty("id").GetGuid();

        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Criterion A", dimension = "Technical", weight = 70, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار ب", nameEn = "Criterion B", dimension = "Commercial", weight = 30, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });

        var activate = await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await activate.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Editing_a_referenced_template_is_rejected_and_forking_produces_a_new_editable_version()
    {
        var manager = await ManagerClientAsync();
        var template = await CreateTemplateAsync(manager, "Referenced Template");
        var templateId = template.GetProperty("id").GetGuid();

        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Criterion A", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        // Bind it to a real RFQ so IsReferenced actually becomes true through the real cross-
        // aggregate path (BindEvaluationTemplateHandler), not by poking the flag directly.
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var rfqResponse = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "RFQ For Binding", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        rfqResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rfq = await rfqResponse.Content.ReadFromJsonAsync<JsonElement>();
        var referenceCode = rfq.GetProperty("referenceCode").GetString();

        var bind = await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template", new { evaluationTemplateId = templateId });
        bind.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now the template is genuinely referenced - a further edit must be refused.
        var editAttempt = await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "لاحق", nameEn = "Too Late", dimension = "Delivery", weight = 10, maxScore = 10,
            threshold = (int?)null, scoringType = "Boolean", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        editAttempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var editBody = await editAttempt.Content.ReadFromJsonAsync<JsonElement>();
        editBody.GetProperty("detail").GetString().Should().Contain("immutable");

        // Forking produces a new, independent version that IS editable.
        var forkResponse = await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/fork", null);
        forkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var forked = await forkResponse.Content.ReadFromJsonAsync<JsonElement>();
        var forkedId = forked.GetProperty("id").GetGuid();
        forkedId.Should().NotBe(templateId);
        forked.GetProperty("version").GetInt32().Should().Be(2);
        forked.GetProperty("isReferenced").GetBoolean().Should().BeFalse();

        var editForkedAttempt = await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{forkedId}/criteria", new
        {
            nameAr = "جديد", nameEn = "New On Fork", dimension = "Delivery", weight = 10, maxScore = 10,
            threshold = (int?)null, scoringType = "Boolean", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        editForkedAttempt.StatusCode.Should().Be(HttpStatusCode.OK);

        // And the RFQ, which bound to the ORIGINAL version, still references it exactly -
        // unaffected by the later fork.
        var rfqAfterFork = await officer.GetFromJsonAsync<JsonElement>($"/api/v1/rfqs/{referenceCode}");
        rfqAfterFork.GetProperty("evaluationTemplateId").GetGuid().Should().Be(templateId);
        rfqAfterFork.GetProperty("evaluationTemplateVersion").GetInt32().Should().Be(1);
    }
}
