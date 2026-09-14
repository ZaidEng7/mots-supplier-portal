// Reading a tender is gated on a read permission, not on the permission to author one.
//
//
// THE DEFECT THESE COVER
//
// The buyer list, detail and workspace reads were gated on the authoring permission, which the manager role
// deliberately does not hold: the written process makes that role the actor for approval, and it is granted review,
// approval and cancellation with no authoring rights.
//
// So the person required to approve a tender could not list one, open one, or see the workspace they approve from.
//
// It survived because no test ever had a manager perform a read: every manager in the suite only posts a
// transition.
//
//
// WHY THE FIX IS A NEW PERMISSION AND NOT A WIDER GRANT
//
// Giving the manager authoring rights would collapse the segregation of duties the award-approval chain depends on.
//
// One test asserts that directly: if a later change "fixes" a manager's refusal by widening the authoring grant,
// it fails. That is the whole point of separating read from create rather than widening what already existed.
//
// The workspace shares the detail's gate by design and moved with it, or it would still lock out the manager for
// whom the guided workspace's approval stage exists.
//
//
// THE TWO OTHER ROLES ARE ASSERTED, NOT ASSUMED
//
// The officer already worked, and splitting a permission is exactly where a role that reached a route through the
// OLD one gets silently dropped.
//
// And an evaluator holds neither permission, reaching a tender only through the assignment-scoped route, so the
// buyer detail must stay closed to them. Without that, "grant it to every role that reads tenders" could quietly
// become "grant it to every back-office role".

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Tests.Integration;

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

    [Fact]
    public async Task A_procurement_manager_can_read_the_guided_workspace()
    {
        var (manager, code) = await SeededOrgAsync();

        var response = await manager.GetAsync($"/api/v1/rfqs/{code}/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_procurement_manager_still_cannot_create_an_rfq()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var response = await manager.PostAsJsonAsync("/api/v1/rfqs", RfqBasics("Manager authored"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "approvers must not gain authoring rights - EPIC-14's approval chain depends on the split");
    }

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

    [Fact]
    public async Task An_evaluator_cannot_read_the_buyer_rfq_detail()
    {
        var (_, code) = await SeededOrgAsync();
        var (evaluator, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator);

        var response = await evaluator.GetAsync($"/api/v1/rfqs/{code}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
