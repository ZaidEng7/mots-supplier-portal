// The ministry's supplier feed over a real database and a real HTTP response.
//
// THE FIRST LINE OF THE FILE IS THE HEADER ROW, and that is the assertion this file exists for. Every other
// export in this product opens with three "#" provenance lines, which are right for a person opening a file in
// Excel and wrong for the nightly loader this feed is written for: a loader takes line one as the column names,
// so it would read "# MOTS Supplier Portal" as the header and every row after it as data. The test asserts the
// absence of something, which is unusual, and it is deliberate - copying the registry export's opening is the
// obvious mistake here, and nothing else would catch it.
//
// THE PROVENANCE IS STILL SENT, in X-Feed-Generated-At and X-Feed-Scope, and both are asserted. Once the file
// is loaded into a warehouse nothing in the rows says when it was taken or that it covers every onboarding
// state rather than only approved suppliers, and a feed from last March read as current is a worse failure
// than no feed at all. Moving those facts to headers keeps them without breaking the parse.
//
// THE PERMISSION MATRIX is the same shape as the registry export's and for the same reason: this is the whole
// registry with tax identifiers and named contacts in one file, so the roles that browse the directory a row at
// a time are refused, and the administrator is asserted to SUCCEED so the matrix cannot pass against a route
// that does not exist.
//
// A DRAFT SUPPLIER IS ASSERTED PRESENT, because their feed carries ApprovalStatus as a column and a file that
// quietly held only approved suppliers would be loaded as the whole registry.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistrySupplierFeedTests(PostgresApiFixture fixture)
{
    private const string Feed = "/api/v1/feeds/suppliers";

    [Fact]
    public async Task The_file_opens_with_the_header_row_and_carries_its_provenance_in_headers()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.GetAsync(Feed);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var firstLine = text.TrimStart('﻿').Split('\n')[0].TrimEnd('\r');

        bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF]);
        firstLine.Should().Be(
            MinistrySupplierFeedCsv.Header,
            "their loader takes line one as the column names, so a provenance comment there would be read as "
            + "the header and every row after it as data");
        text.Should().NotContain("# MOTS Supplier Portal");

        response.Headers.GetValues("X-Feed-Scope").Should().ContainSingle()
            .Which.Should().Be(Api.Endpoints.MinistryFeedEndpoints.SupplierScope);
        response.Headers.GetValues("X-Feed-Generated-At").Should().ContainSingle();
    }

    [Fact]
    public async Task Only_the_system_administrator_may_read_the_feed()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(
            fixture, $"Feed Outsider {Guid.NewGuid():N}"[..20]);

        (await officer.GetAsync(Feed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ministry.GetAsync(Feed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supplier.GetAsync(Feed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.GetAsync(Feed)).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "without this the refusals above would pass just as well against a route that does not exist");
    }

    [Fact]
    public async Task A_supplier_at_any_onboarding_state_appears_with_its_translated_status()
    {
        var draftName = $"Feed Draft {Guid.NewGuid():N}"[..20];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, draftName);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.DisplayNameEn == draftName)
                .ExecuteUpdateAsync(p => p
                    .SetProperty(s => s.OnboardingState, SupplierOnboardingState.UnderReview));
        }

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var text = await (await admin.GetAsync(Feed)).Content.ReadAsStringAsync();
        var row = text.Split('\n').Single(l => l.Contains(draftName, StringComparison.Ordinal));

        row.Should().Contain(
            "Pending Financial Approval",
            "a file that quietly held only approved suppliers would be loaded as the whole registry");
    }
}
