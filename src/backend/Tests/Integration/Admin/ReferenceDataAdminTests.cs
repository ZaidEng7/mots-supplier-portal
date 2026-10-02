// The reference tables were seed-only, so a ministry could not add a document type without a deployment.
//
// The administrator needs a second factor to obtain a session at all, so the plain helper is refused.
//
//
// DEACTIVATE, NEVER DELETE
//
// A category a published tender points at must not be removable, which is the whole reason.
//
// So a deactivated row disappears from the supplier-facing read while the ROW still exists, which is the difference
// between deactivation and deletion. The administrator can still see it, because otherwise deactivation reads as
// deletion and the next administrator re-creates the code. And it is reversible, with its own audit action.
//
// No delete route exists at all, and the assertion accepts either answer the framework gives for that: the point is
// that it is not a success.
//
//
// WHAT A WRITE MUST AND MUST NOT DO
//
// Omitted means NOT required, and that matters: required by default would retroactively make every existing
// supplier's profile incomplete the moment the row was created.
//
// It is asserted against storage rather than the response it just echoed, and it is audited, because a write here is
// a governance act.
//
// The same code twice is a conflict rather than a silent overwrite of somebody's row. A code longer than the column
// is refused naming the limit rather than producing a server error from the database, which is how this was found:
// by sending one too long.
//
//
// THE UNKNOWN-TABLE CASE NEEDS A NAME THAT IS GENUINELY NOT ONE
//
// A sixth table has since been added, so a misspelling is the realistic shape of this mistake and the one that must
// not resolve to a neighbouring table. Its control is a real table on the same route family answering.
//
// The permission negative has an owner control too, because it covers the whole catalogue every supplier registers
// against.
//
//
// THE SIXTH TABLE
//
// The row this replaces asserted a not-found on this very route, with the reason that the requirement named the
// table and no entity existed. That was an honest record of a gap and is now the wrong assertion.
//
// Its eleven standard terms are named one by one rather than counted, because a count passes against eleven of
// anything, and the set belongs to the standards body rather than to this product to trim.
//
// A term typed in lower case is stored in upper case, and every later step must find it by the code it was typed
// with. Only the stored row used to be upper-cased, so creating "dpu" saved DPU and then answered not-found. The
// test sends lower case on every write, then reads storage and the audit rows for the upper-case code: a fix that
// upper-cased the write but not the read-back, the lookup or the audit row fails one of them. The same lower-case
// code sent twice must be a conflict: a duplicate check that compared the typed code would let it through to the
// unique index, which answers 500. The upper-case duplicate is the control that both spellings name one row.
//
// The row is removed afterwards. A run that failed before the deactivation would otherwise leave a twelfth active
// term, and the eleven-term test above reads the whole table.

namespace MotsSupplierPortal.Tests.Integration.Admin;

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
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ReferenceDataAdminTests(PostgresApiFixture fixture)
{
    private async Task<HttpClient> AdminAsync() =>
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

        body.GetProperty("isRequired").GetBoolean().Should().BeFalse();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<DocumentType>().AsNoTracking().FirstAsync(d => d.Code == code);
        stored.IsActive.Should().BeTrue();
        stored.ExpiryTracked.Should().BeTrue();

        (await db.AuditLogs.AnyAsync(a => a.ReferenceCode == code && a.Action == "reference.document-types.created"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Deactivating_hides_a_code_from_new_use_and_leaves_the_rows_that_point_at_it_alone()
    {
        var admin = await AdminAsync();
        var code = $"cat-{Guid.NewGuid():N}"[..16];

        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/categories/{code}", new
        {
            nameAr = "تصنيف", nameEn = "Category", isRequired = (bool?)null, expiryTracked = (bool?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var publicList = await fixture.CreateRawClient().GetFromJsonAsync<JsonElement>("/api/v1/reference/categories");
        publicList.EnumerateArray().Select(c => c.GetProperty("code").GetString())
            .Should().Contain(code, "an active category is offered");

        var deactivated = await admin.PostAsync($"/api/v1/admin/reference/categories/{code}/deactivate", null);
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterPublic = await fixture.CreateRawClient().GetFromJsonAsync<JsonElement>("/api/v1/reference/categories");
        afterPublic.EnumerateArray().Select(c => c.GetProperty("code").GetString())
            .Should().NotContain(code, "a deactivated category is not offered for new selections");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<Category>().AsNoTracking().AnyAsync(c => c.Code == code))
                .Should().BeTrue("D-28: the row survives so live rows pointing at this code stay readable");
        }

        var adminList = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/admin/reference/categories?includeInactive=true");
        adminList.EnumerateArray().Select(c => c.GetProperty("code").GetString()).Should().Contain(code);

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
        var code = $"X{Random.Shared.Next(10, 99)}";

        var payload = new { nameAr = "عملة", nameEn = "Currency", isRequired = (bool?)null, expiryTracked = (bool?)null };
        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/currencies/{code}", payload))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/currencies/{code}", payload))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var tooLong = await admin.PostAsJsonAsync("/api/v1/admin/reference/currencies/TOOLONG", payload);
        tooLong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("3 characters");

        var deleted = await admin.DeleteAsync($"/api/v1/admin/reference/currencies/{code}");
        deleted.IsSuccessStatusCode.Should().BeFalse(
            "deletion would orphan every live row pointing at this code; deactivation is the only removal");
    }

    [Fact]
    public async Task An_unknown_table_is_a_404_rather_than_a_write_against_the_wrong_one()
    {
        var admin = await AdminAsync();

        (await admin.GetAsync("/api/v1/admin/reference/incoterm"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "a typo or a missing table must not silently resolve to a different one");

        (await admin.GetAsync("/api/v1/admin/reference/regions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_without_the_permission_can_write_reference_data()
    {
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

        var admin = await AdminAsync();
        (await admin.PostAsJsonAsync($"/api/v1/admin/reference/regions/{code}", new
        {
            nameAr = "منطقة", nameEn = "Region", isRequired = (bool?)null, expiryTracked = (bool?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_incoterm_table_carries_the_eleven_terms_of_the_standard()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync("/api/v1/admin/reference/incoterms");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var codes = (await response.Content.ReadFromJsonAsync<List<JsonElement>>())!
            .Select(i => i.GetProperty("code").GetString()).ToList();

        codes.Should().BeEquivalentTo(new[]
        {
            "EXW", "FCA", "CPT", "CIP", "DAP", "DPU", "DDP", "FAS", "FOB", "CFR", "CIF",
        });
    }

    [Fact]
    public async Task A_ministry_narrows_the_standard_by_deactivating_a_term_rather_than_deleting_it()
    {
        var admin = await AdminAsync();

        try
        {
            var deactivated = await admin.PostAsJsonAsync("/api/v1/admin/reference/incoterms/FAS/deactivate", new { });
            deactivated.StatusCode.Should().Be(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());

            var offered = (await (await admin.GetAsync("/api/v1/reference/incoterms")).Content.ReadFromJsonAsync<List<JsonElement>>())!
                .Select(i => i.GetProperty("code").GetString()).ToList();
            offered.Should().NotContain("FAS", "a deactivated term is not offered to a bidder");
            offered.Should().Contain("FOB", "and the rest of the standard is untouched");

            var all = (await (await admin.GetAsync("/api/v1/admin/reference/incoterms?includeInactive=true")).Content.ReadFromJsonAsync<List<JsonElement>>())!;
            all.Should().Contain(i => i.GetProperty("code").GetString() == "FAS");
        }
        finally
        {
            await admin.PostAsJsonAsync("/api/v1/admin/reference/incoterms/FAS/reactivate", new { });
        }
    }

    [Fact]
    public async Task An_incoterm_typed_in_lower_case_is_stored_audited_and_found_in_upper_case()
    {
        var admin = await AdminAsync();
        var upper = $"Q{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}";
        var lower = upper.ToLowerInvariant();
        var payload = new { nameAr = "شرط تسليم", nameEn = "Delivery term", isRequired = (bool?)null, expiryTracked = (bool?)null };

        try
        {
            var created = await admin.PostAsJsonAsync($"/api/v1/admin/reference/incoterms/{lower}", payload);
            created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
            (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(upper);

            (await admin.PostAsJsonAsync($"/api/v1/admin/reference/incoterms/{lower}", payload))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, "the same code typed twice is a duplicate, not a server error");
            (await admin.PostAsJsonAsync($"/api/v1/admin/reference/incoterms/{upper}", payload))
                .StatusCode.Should().Be(HttpStatusCode.Conflict, "both spellings name the one row just created");

            var renamed = await admin.PutAsJsonAsync($"/api/v1/admin/reference/incoterms/{lower}", new
            {
                nameAr = "شرط تسليم معدّل", nameEn = "Delivery term renamed", isRequired = (bool?)null, expiryTracked = (bool?)null,
            });
            renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());

            var deactivated = await admin.PostAsJsonAsync($"/api/v1/admin/reference/incoterms/{lower}/deactivate", new { });
            deactivated.StatusCode.Should().Be(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());

            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Set<Incoterm>().AsNoTracking().Where(i => i.Code == upper || i.Code == lower).ToListAsync();
            stored.Should().ContainSingle().Which.Code.Should().Be(upper);
            stored[0].NameEn.Should().Be("Delivery term renamed");
            stored[0].IsActive.Should().BeFalse();

            var audited = await db.AuditLogs.AsNoTracking()
                .Where(a => a.ReferenceCode == upper || a.ReferenceCode == lower)
                .Where(a => a.Action.StartsWith("reference.incoterms."))
                .Select(a => new { a.Action, a.ReferenceCode })
                .ToListAsync();
            audited.Should().BeEquivalentTo(new[]
            {
                new { Action = "reference.incoterms.created", ReferenceCode = (string?)upper },
                new { Action = "reference.incoterms.updated", ReferenceCode = (string?)upper },
                new { Action = "reference.incoterms.deactivated", ReferenceCode = (string?)upper },
            });
        }
        finally
        {
            await using var cleanup = fixture.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<Incoterm>().Where(i => i.Code == upper || i.Code == lower).ExecuteDeleteAsync();
        }
    }
}
