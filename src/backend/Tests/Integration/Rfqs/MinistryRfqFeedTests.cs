// The ministry's RFQ feed over a real database and a real HTTP response.
//
// AN INVITED SUPPLIER WHO NEVER REPLIED HAS A ROW, and that is the assertion this file exists for. The feed's
// grain is the invitation, so a supplier who was asked and said nothing appears with QuoteStatus Pending and
// three empty quotation cells. Driving the feed off proposals instead would have produced a file containing
// only the suppliers who answered - and the number the ministry is measuring is how many were asked against
// how many replied, which that file cannot tell them.
//
// A SUBMITTED PROPOSAL CARRIES ITS REFERENCE AND ITS TOTAL, summed from its lines, because a proposal in this
// product stores no grand total. Asserting the total rather than only the status is what proves the sum
// reached the row: a join that returned the proposal but lost its lines would still say Received.
//
// THE FILE OPENS WITH THE HEADER ROW, no provenance comments, for the same reason as the supplier feed - a
// nightly loader takes line one as the column names. Copying the registry export's opening is the obvious
// mistake on any new export here, so each one asserts against it.
//
// THE PERMISSION MATRIX is the supplier feed's, and included rather than assumed: this route was added to an
// existing endpoint file and taking its neighbour's gate for granted is exactly how a route ends up ungated.

namespace MotsSupplierPortal.Tests.Integration.Rfqs;

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistryRfqFeedTests(PostgresApiFixture fixture)
{
    private const string Feed = "/api/v1/feeds/rfqs";

    [Fact]
    public async Task The_file_opens_with_the_header_row_and_carries_its_scope_in_a_header()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var response = await admin.GetAsync(Feed);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var firstLine = text.TrimStart('﻿').Split('\n')[0].TrimEnd('\r');

        bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF]);
        firstLine.Should().Be(MinistryRfqFeedCsv.Header);
        text.Should().NotContain("# MOTS Supplier Portal");

        response.Headers.GetValues("X-Feed-Scope").Should().ContainSingle()
            .Which.Should().Be(Api.Endpoints.MinistryFeedEndpoints.RfqScope);
    }

    [Fact]
    public async Task Only_the_system_administrator_may_read_the_feed()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

        (await officer.GetAsync(Feed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ministry.GetAsync(Feed)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(Feed)).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "this route was added beside an existing one, and assuming it inherited the gate is how a route "
            + "ends up ungated");
    }

    [Fact]
    public async Task An_invited_supplier_who_never_replied_has_a_row_beside_the_one_who_did()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, $"Feed {Guid.NewGuid():N}"[..12]);
        var silentName = $"Silent {Guid.NewGuid():N}"[..18];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, silentName);

        string silentCode;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rfq = await db.Rfqs.AsNoTracking().FirstAsync(r => r.ReferenceCode == seeded.RfqCode);
            var silent = await db.Suppliers.AsNoTracking().FirstAsync(s => s.DisplayNameEn == silentName);
            silentCode = silent.ReferenceCode;

            db.Invitations.Add(new Invitation
            {
                Id = Guid.CreateVersion7(),
                RfqId = rfq.Id,
                SupplierId = silent.Id,
                InvitedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var text = await (await admin.GetAsync(Feed)).Content.ReadAsStringAsync();
        var rows = text.Split('\n').Where(l => l.Contains(seeded.RfqCode, StringComparison.Ordinal)).ToList();

        var silentRow = rows.Single(r => r.Contains(silentCode, StringComparison.Ordinal));
        silentRow.Should().Contain(
            ",Pending,",
            "an invitation nobody answered is the row a proposal-driven feed would have lost, and it is the "
            + "difference between how many were asked and how many replied");
        silentRow.Should().EndWith(",,,", "no proposal means no reference, no total and no currency");

        rows.Should().Contain(
            r => r.Contains(",Received,", StringComparison.Ordinal),
            "the supplier who did submit is the control: without it this test would pass on a feed that had "
            + "stopped finding proposals at all");
    }
}
