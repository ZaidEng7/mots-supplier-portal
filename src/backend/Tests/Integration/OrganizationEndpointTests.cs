using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>Task #7/Stage C: the admin-only Organization/OrgUnit/SupplierOrgLink surface. Proves
/// the endpoints work end-to-end through the real HTTP contract (not just unit-level), that the
/// admin.organizations.manage permission gate actually rejects a caller without it, and that no
/// link is ever created except by the explicit CreateSupplierOrgLink call - the "no auto-linking"
/// guarantee this stage's ticket required.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OrganizationEndpointTests(PostgresApiFixture fixture)
{
    // system_admin requires MFA (NFR-SEC-003) - CreateWithMfaAsync, not the plain-password
    // CreateAsync, or login itself fails 403 before ever reaching the endpoint under test.
    private Task<HttpClient> AdminClientAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    [Fact]
    public async Task Create_organization_succeeds_for_an_authorized_caller_and_is_listed()
    {
        var admin = await AdminClientAsync();

        var createResponse = await admin.PostAsJsonAsync("/api/v1/organizations", new
        {
            legalNameAr = "وزارة الاختبار",
            legalNameEn = "Test Ministry",
            organizationType = "Ministry",
            contactEmail = "contact@ministry.example",
            contactPhone = "+963900000000",
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("legalNameEn").GetString().Should().Be("Test Ministry");
        created.GetProperty("organizationType").GetString().Should().Be("Ministry");
        var orgId = created.GetProperty("id").GetString();

        var listResponse = await admin.GetAsync("/api/v1/organizations");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        list.Should().Contain(o => o.GetProperty("id").GetString() == orgId);
    }

    [Fact]
    public async Task Create_organization_rejects_a_caller_without_the_permission()
    {
        // onboarding_reviewer has real permissions (SupplierApprove etc.) but not
        // admin.organizations.manage - proves this is a genuine authorization check, not "any
        // authenticated staff user can do this".
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var response = await reviewer.PostAsJsonAsync("/api/v1/organizations", new
        {
            legalNameAr = "test",
            legalNameEn = "test",
            organizationType = "Hotel",
            contactEmail = (string?)null,
            contactPhone = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Add_and_remove_an_OrgUnit_under_an_organization()
    {
        var admin = await AdminClientAsync();
        var org = await CreateOrganizationAsync(admin, "Org Unit Test Co");

        var addResponse = await admin.PostAsJsonAsync($"/api/v1/organizations/{org}/org-units", new { name = "Procurement Committee", parentOrgUnitId = (Guid?)null });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterAdd = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var units = afterAdd.GetProperty("orgUnits").EnumerateArray().ToList();
        units.Should().ContainSingle();
        var unitId = units[0].GetProperty("id").GetGuid();

        var removeResponse = await admin.DeleteAsync($"/api/v1/organizations/{org}/org-units/{unitId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterRemove = await removeResponse.Content.ReadFromJsonAsync<JsonElement>();
        afterRemove.GetProperty("orgUnits").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_an_OrgUnit_with_children_is_refused()
    {
        var admin = await AdminClientAsync();
        var org = await CreateOrganizationAsync(admin, "Org Unit Tree Test Co");

        var parentResponse = await admin.PostAsJsonAsync($"/api/v1/organizations/{org}/org-units", new { name = "Parent", parentOrgUnitId = (Guid?)null });
        var parent = (await parentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orgUnits").EnumerateArray().Single().GetProperty("id").GetGuid();

        await admin.PostAsJsonAsync($"/api/v1/organizations/{org}/org-units", new { name = "Child", parentOrgUnitId = parent });

        var removeParent = await admin.DeleteAsync($"/api/v1/organizations/{org}/org-units/{parent}");
        removeParent.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_a_SupplierOrgLink_and_it_appears_in_the_supplier_s_link_list()
    {
        var admin = await AdminClientAsync();
        var org = await CreateOrganizationAsync(admin, "Link Test Org");
        var referenceCode = await CreateSupplierReferenceCodeAsync("Link Test Supplier");

        var createLink = await admin.PostAsJsonAsync($"/api/v1/organizations/supplier-links/{referenceCode}", new { organizationId = org });
        createLink.StatusCode.Should().Be(HttpStatusCode.OK);

        var listLinks = await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/supplier-links/{referenceCode}");
        listLinks.Should().ContainSingle(l => l.GetProperty("organizationId").GetGuid() == org);
    }

    [Fact]
    public async Task Creating_the_same_SupplierOrgLink_twice_is_rejected_as_a_conflict()
    {
        var admin = await AdminClientAsync();
        var org = await CreateOrganizationAsync(admin, "Duplicate Link Org");
        var referenceCode = await CreateSupplierReferenceCodeAsync("Duplicate Link Supplier");

        await admin.PostAsJsonAsync($"/api/v1/organizations/supplier-links/{referenceCode}", new { organizationId = org });
        var second = await admin.PostAsJsonAsync($"/api/v1/organizations/supplier-links/{referenceCode}", new { organizationId = org });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Removing_a_SupplierOrgLink_removes_it_from_the_list()
    {
        var admin = await AdminClientAsync();
        var org = await CreateOrganizationAsync(admin, "Remove Link Org");
        var referenceCode = await CreateSupplierReferenceCodeAsync("Remove Link Supplier");

        var createLink = await admin.PostAsJsonAsync($"/api/v1/organizations/supplier-links/{referenceCode}", new { organizationId = org });
        var linkId = (await createLink.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var removeResponse = await admin.DeleteAsync($"/api/v1/organizations/supplier-links/{linkId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/supplier-links/{referenceCode}");
        listAfter.Should().BeEmpty();
    }

    /// <summary>Task #7/Stage C's "no auto-linking" guarantee: a freshly registered, verified,
    /// fully onboarded supplier - going through every real state transition, not a shortcut - has
    /// zero SupplierOrgLink rows until an admin explicitly creates one. Nothing in the onboarding/
    /// registration/approval path may ever create a link as a side effect.</summary>
    [Fact]
    public async Task A_newly_registered_supplier_has_no_organization_links_until_one_is_explicitly_created()
    {
        var referenceCode = await CreateSupplierReferenceCodeAsync("No Auto Link Supplier");
        var admin = await AdminClientAsync();

        var links = await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/organizations/supplier-links/{referenceCode}");

        links.Should().BeEmpty();
    }

    private static async Task<Guid> CreateOrganizationAsync(HttpClient admin, string nameEn)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/organizations", new
        {
            legalNameAr = nameEn,
            legalNameEn = nameEn,
            organizationType = "Hotel",
            contactEmail = (string?)null,
            contactPhone = (string?)null,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<string> CreateSupplierReferenceCodeAsync(string displayNameEn)
    {
        var supplierClient = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, displayNameEn);
        var me = await supplierClient.GetFromJsonAsync<JsonElement>("/api/v1/suppliers/me");
        return me.GetProperty("supplierCode").GetString()!;
    }
}
