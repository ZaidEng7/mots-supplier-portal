using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// SCR-604: category and sector coverage, for the Ministry.
///
/// <para><b>One of the two Ministry screens that were never refused and were absent anyway</b> (T-100).
/// SCR-601/602/603/606 are held back by a disclosure decision - D-57, still awaiting a signature. This one
/// never needed it: every figure is a count, so it sits squarely inside BRULE-086's aggregate grant, and
/// nothing on it is commercial.</para>
///
/// <para><b>The empty categories are the point,</b> which is why the first test seeds a category nobody
/// serves and requires it to be listed. A coverage screen built from the LINK table would answer "which
/// categories have suppliers" while appearing to answer "which categories exist" - and the difference is
/// invisible unless something asserts it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CategoryCoverageTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/ministry/categories";

    private static JsonElement Row(JsonElement body, string code) =>
        body.GetProperty("categories").EnumerateArray()
            .First(c => c.GetProperty("categoryCode").GetString() == code);

    [Fact]
    public async Task A_category_nobody_serves_is_listed_with_zeroes_rather_than_omitted()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var code = $"cov_{Guid.NewGuid():N}"[..16];
        var created = await admin.PostAsJsonAsync($"/api/v1/admin/reference/categories/{code}", new
        {
            nameAr = "فئة بلا موردين", nameEn = "Uncovered category", isRequired = (bool?)null, expiryTracked = (bool?)null,
        });
        created.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            await created.Content.ReadAsStringAsync());

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var body = await ministry.GetFromJsonAsync<JsonElement>(Route);

        var row = Row(body, code);
        row.GetProperty("approvedSuppliers").GetInt32().Should().Be(0);
        row.GetProperty("activeSuppliers").GetInt32().Should().Be(0);
        row.GetProperty("tenders").GetInt32().Should().Be(0);

        body.GetProperty("categoriesWithNoActiveSupplier").GetInt32().Should().BeGreaterThan(0,
            "the count is computed on the server because counting empty rows by eye is how a dashboard "
            + "becomes decoration");

        // Said on the response rather than drawn as a tree: MSP-54's list is flat, and SCR-604's own row in
        // the inventory says "category tree". A screen inventing the hierarchy would be inventing policy.
        body.GetProperty("categoriesAreFlat").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_suspended_supplier_counts_as_approved_and_not_as_active()
    {
        // The distinction the two columns exist for. A category whose only supplier is suspended reads as
        // covered on any screen that carries one number, and the market is in fact unserved.
        var name = $"Cov {Guid.NewGuid():N}"[..20];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var code = $"cov_{Guid.NewGuid():N}"[..16];
        await admin.PostAsJsonAsync($"/api/v1/admin/reference/categories/{code}", new
        {
            nameAr = "فئة موقوفة", nameEn = "Suspended-only category", isRequired = (bool?)null, expiryTracked = (bool?)null,
        });

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).FirstAsync();
            db.CategoryLinks.Add(new CategoryLink { Id = Guid.CreateVersion7(), SupplierId = supplierId, CategoryCode = code });
            await db.SaveChangesAsync();

            // Second scope's worth of work in one place would write the tracked supplier's old states back
            // over the update - the trap SupplierDirectoryTests documents. Separate save, separate context.
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.DisplayNameEn == name).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Suspended));
        }

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var row = Row(await ministry.GetFromJsonAsync<JsonElement>(Route), code);

        row.GetProperty("approvedSuppliers").GetInt32().Should().Be(1);
        row.GetProperty("activeSuppliers").GetInt32().Should().Be(0,
            "suspension is exactly what the two columns are here to make visible");
    }

    [Fact]
    public async Task It_names_no_supplier_no_tender_and_no_value()
    {
        // BRULE-086's own words are "aggregate/governance metrics only". This asserts the shape rather than
        // trusting the handler's intent: a field added later that carried a name would fail here.
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var raw = await ministry.GetStringAsync(Route);

        raw.Should().NotContain("SUP-2026-", "a supplier reference code is an identity, not an aggregate");
        raw.Should().NotContain("RFQ-2026-");
        raw.Should().NotContain("displayName");
        raw.Should().NotContain("value", "no figure on this read is commercial, so none should be named one");
    }

    [Fact]
    public async Task Only_the_governance_persona_may_read_it()
    {
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"CovOut {Guid.NewGuid():N}"[..20]);

        // governance.read, not report.read or rfq.read: this route skips organization scoping, and a route
        // that skips row-scoping must be reachable only by the persona whose purpose is to skip it.
        (await officer.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reviewer.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supplier.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        (await ministry.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.OK,
            "and the control, so the three refusals above are about permission rather than a broken route");
    }
}
