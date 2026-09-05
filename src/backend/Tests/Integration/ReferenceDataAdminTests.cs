using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-034/T-059/FR-ADM-004: five reference tables were seed-only, so a ministry could not add a
/// document type without a deploy.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReferenceDataAdminTests(PostgresApiFixture fixture)
{
    private async Task<HttpClient> AdminAsync() =>
        // system_admin needs MFA to obtain a session; CreateAsync 403s.
        await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    [Fact]
    public async Task An_admin_can_add_a_document_type_and_it_appears_where_suppliers_read_it()
    {
        var admin = await AdminAsync();
        var code = $"TEST_TYPE_{Guid.NewGuid():N}"[..20];

        var created = await admin.PostAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
        {
            nameAr = "شهادة اختبار", nameEn = "Test certificate",
            isRequired = (bool?)null, expiryTracked = true,
        });

        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("expiryTracked").GetBoolean().Should().BeTrue();

        // Omitted means NOT required, and that matters: required-by-default would retroactively make
        // every existing supplier's profile incomplete the moment this row was created.
        body.GetProperty("isRequired").GetBoolean().Should().BeFalse();

        // Asserted against storage, not the response it just echoed.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<DocumentType>().AsNoTracking().FirstAsync(d => d.Code == code);
        stored.IsActive.Should().BeTrue();
        stored.ExpiryTracked.Should().BeTrue();

        // FR-ADM-004's point: a write is a governance act, so it is audited.
        (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == code && a.Action == "reference.document-types.created"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Deactivating_hides_a_code_from_new_use_and_leaves_the_rows_that_point_at_it_alone()
    {
        // D-28's whole reason. A category a published RFQ item points at must not be removable.
        var admin = await AdminAsync();
        var code = $"cat-{Guid.NewGuid():N}"[..16];

        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/categories/{code}", new
        {
            nameAr = "تصنيف", nameEn = "Category", isRequired = (bool?)null, expiryTracked = (bool?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Visible to a supplier-facing read while active.
        var publicList = await fixture.CreateRawClient().GetFromJsonAsync<JsonElement>("/api/v1/reference/categories");
        publicList.EnumerateArray().Select(c => c.GetProperty("code").GetString())
            .Should().Contain(code, "an active category is offered");

        var deactivated = await admin.PostAsync($"/api/v1/admin/reference/categories/{code}/deactivate", null);
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        // Gone from the supplier-facing read...
        var afterPublic = await fixture.CreateRawClient().GetFromJsonAsync<JsonElement>("/api/v1/reference/categories");
        afterPublic.EnumerateArray().Select(c => c.GetProperty("code").GetString())
            .Should().NotContain(code, "a deactivated category is not offered for new selections");

        // ...and the ROW still exists, which is the difference between deactivation and deletion.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<Category>().AsNoTracking().AnyAsync(c => c.Code == code))
                .Should().BeTrue("D-28: the row survives so live rows pointing at this code stay readable");
        }

        // The admin can still see it - otherwise deactivation reads as deletion and the next
        // administrator re-creates the code.
        var adminList = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/admin/reference/categories?includeInactive=true");
        adminList.EnumerateArray().Select(c => c.GetProperty("code").GetString()).Should().Contain(code);

        // And it is reversible, with its own audit action.
        (await admin.PostAsync($"/api/v1/admin/reference/categories/{code}/reactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actions = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ReferenceCode == code).Select(a => a.Action).ToListAsync();
            actions.Should().Contain("reference.categories.deactivated");
            actions.Should().Contain("reference.categories.reactivated");
        }
    }

    [Fact]
    public async Task There_is_no_delete_and_a_duplicate_code_is_refused()
    {
        var admin = await AdminAsync();
        // Three characters: Currency.Code is ISO-bounded at 3, which the handler now enforces with a
        // 422 rather than letting Postgres answer 500 (found by this test sending eight).
        var code = $"X{Random.Shared.Next(10, 99)}";

        var payload = new { nameAr = "عملة", nameEn = "Currency", isRequired = (bool?)null, expiryTracked = (bool?)null };
        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/currencies/{code}", payload))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Refusable: the same code again is a conflict, not a silent overwrite of someone's row.
        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/currencies/{code}", payload))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // And a code longer than the column is a 422 naming the limit, not a 500 from the database.
        var tooLong = await admin.PostAsJsonAsync("/api/v1/admin/reference/currencies/TOOLONG", payload);
        tooLong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("3 characters");

        // D-28: no DELETE exists at all. 404/405 either way - the point is that it is not a success.
        var deleted = await admin.DeleteAsync($"/api/v1/admin/reference/currencies/{code}");
        deleted.IsSuccessStatusCode.Should().BeFalse(
            "deletion would orphan every live row pointing at this code; deactivation is the only removal");
    }

    [Fact]
    public async Task An_unknown_table_is_a_404_rather_than_a_write_against_the_wrong_one()
    {
        var admin = await AdminAsync();

        (await admin.GetAsync("/api/v1/admin/reference/incoterms"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "FR-ADM-004 names Incoterm but no entity exists - a typo or a missing table must not " +
                "silently resolve to a different one");

        // The control: a real table on the same route family answers.
        (await admin.GetAsync("/api/v1/admin/reference/regions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_without_the_permission_can_write_reference_data()
    {
        // The whole catalogue every supplier registers against - the negative needs an owner control.
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var code = $"reg-{Guid.NewGuid():N}"[..10];

        var refused = await officer.PostAsJsonAsync($"/api/v1/admin/reference/regions/{code}", new
        {
            nameAr = "منطقة", nameEn = "Region", isRequired = (bool?)null, expiryTracked = (bool?)null,
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<Region>().AsNoTracking().AnyAsync(r => r.Code == code)).Should().BeFalse();

        // The control: an admin holding reference.manage does write it.
        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/regions/{code}", new
        {
            nameAr = "منطقة", nameEn = "Region", isRequired = (bool?)null, expiryTracked = (bool?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
