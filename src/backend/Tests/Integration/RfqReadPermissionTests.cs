using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// FEAT-07.1 / BUSINESS-PROCESSES.md §3.1: reading an RFQ is gated on <c>rfq.read</c>, not on
/// <c>rfq.create</c>.
///
/// <para><b>The defect these cover.</b> The buyer list, detail and workspace GETs were gated on
/// <c>rfq.create</c>, which <c>procurement_manager</c> deliberately does not hold - §3.1 makes that
/// role the actor for InternalReview → Approved, and Permissions.cs grants it
/// rfq.review/rfq.approve/rfq.cancel with no authoring rights. So the person required to approve an
/// RFQ could not list one, open one, or see the workspace they approve from. It survived because no
/// test ever had a manager perform a GET: every manager in the suite only POSTs a transition.</para>
///
/// <para><b>Why the fix is a new permission and not a wider grant.</b> Adding rfq.create to the
/// manager would give approvers authoring rights, collapsing the segregation of duties EPIC-14's
/// award-approval chain depends on. The last test here is the one that keeps that honest.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RfqReadPermissionTests(PostgresApiFixture fixture)
{
    private static object RfqBasics(string titleEn) => new
    {
        titleAr = "طلب صلاحيات", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null,
        currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
        submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
        clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
    };

    /// <summary>An officer authors one RFQ so the manager has something real to read.</summary>
    private async Task<(HttpClient Manager, string ReferenceCode)> SeededOrgAsync()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics($"Read perm {Guid.NewGuid():N}"[..24]));
        created.EnsureSuccessStatusCode();
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        return (manager, code);
    }

    [Fact]
    public async Task A_procurement_manager_can_list_rfqs()
    {
        var (manager, code) = await SeededOrgAsync();

        var response = await manager.GetAsync("/api/v1/rfqs?pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the approver must be able to reach the list they approve from");
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString())
            .Should().Contain(code, "and must actually see their own organization's RFQ, not an empty list");
    }

    [Fact]
    public async Task A_procurement_manager_can_read_an_rfq_detail()
    {
        var (manager, code) = await SeededOrgAsync();

        var response = await manager.GetAsync($"/api/v1/rfqs/{code}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("referenceCode").GetString().Should().Be(code);
    }

    /// <summary>
    /// The workspace shares the RFQ detail's gate by design (WorkspaceEndpoints' own doc comment),
    /// so it moved with it - and would otherwise still lock out the manager for whom the guided
    /// workspace's approval stage exists.
    /// </summary>
    [Fact]
    public async Task A_procurement_manager_can_read_the_guided_workspace()
    {
        var (manager, code) = await SeededOrgAsync();

        var response = await manager.GetAsync($"/api/v1/rfqs/{code}/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Segregation of duties, asserted directly. If a later change "fixes" a manager 403 by adding
    /// rfq.create to the grant, this fails - which is the whole point of separating read from
    /// create rather than widening the existing permission.
    /// </summary>
    [Fact]
    public async Task A_procurement_manager_still_cannot_create_an_rfq()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var response = await manager.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Manager authored"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "approvers must not gain authoring rights - EPIC-14's approval chain depends on the split");
    }

    /// <summary>
    /// The officer is the other role granted rfq.read. Splitting a permission is exactly where a
    /// role that previously reached a route through the OLD permission gets silently dropped, so
    /// the role that already worked is asserted too rather than assumed.
    /// </summary>
    [Fact]
    public async Task A_procurement_officer_can_still_list_and_read()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Officer read"));
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        (await officer.GetAsync("/api/v1/rfqs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await officer.GetAsync($"/api/v1/rfqs/{code}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await officer.GetAsync($"/api/v1/rfqs/{code}/workspace")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// rfq.read is a buyer permission and was granted to exactly two roles. An evaluator holds
    /// neither it nor rfq.create, and reaches an RFQ only through the assignment-scoped
    /// my-evaluation route - so the buyer detail must stay closed to them. Without this, "grant it
    /// to every role that reads RFQs" could quietly become "grant it to every back-office role".
    /// </summary>
    [Fact]
    public async Task An_evaluator_cannot_read_the_buyer_rfq_detail()
    {
        var (_, code) = await SeededOrgAsync();
        var (evaluator, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);

        var response = await evaluator.GetAsync($"/api/v1/rfqs/{code}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
