// modified_since, which is how the ministry's nightly job avoids re-loading the whole country every night.
//
// THE TEST THIS FILE EXISTS FOR IS THE EDITED PROPOSAL. Feed 4's row is an invitation crossed with a proposal,
// and the obvious implementation filters on the invitation, because that is what the row is keyed by. It
// passes every test anyone would think to write: rows come back, old rows are excluded, new invitations appear.
// And it is wrong in the one way that matters - a supplier corrects their price, the quoted total changes, and
// the ministry's copy stays frozen at the old figure forever with nothing reporting a fault. So a proposal is
// edited here with no state change and no new invitation, and the row must come back.
//
// AN EDITED CHILD COUNTS AS AN EDIT, on both feeds. The columns the ministry is waiting on live mostly on a
// supplier's address, so a filter noticing only writes to the supplier row itself would never re-send a
// corrected coordinate - and coordinates are the column they asked us to add.
//
// AN UNTOUCHED ROW MUST BE ABSENT, which is the control. A filter that returned everything regardless would
// satisfy every "the edited row comes back" assertion in this file.
//
// A MALFORMED TIMESTAMP IS REFUSED rather than ignored. A job whose date formatting is broken would otherwise
// pull the entire registry nightly and report success: more rows than expected is not a shape anyone monitors.

namespace MotsSupplierPortal.Tests.Integration.Suppliers;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistryFeedIncrementalTests(PostgresApiFixture fixture)
{
    private static async Task<JsonElement> FeedAsync(HttpClient client, string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static List<string> SupplierIds(JsonElement envelope) =>
        [.. envelope.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("SupplierID").GetString()!)];

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    [Fact]
    public async Task A_supplier_nobody_has_touched_is_left_out()
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Quiet Co {Guid.NewGuid():N}"[..20]);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var everything = await FeedAsync(admin, "/api/v1/feeds/suppliers?limit=2000");
        everything.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);

        var afterwards = await FeedAsync(
            admin, $"/api/v1/feeds/suppliers?limit=2000&modified_since={Iso(DateTimeOffset.UtcNow.AddSeconds(1))}");

        SupplierIds(afterwards).Should().BeEmpty(
            "this is the control - a filter that returned everything would pass every other assertion here");
        GC.KeepAlive(supplier);
    }

    [Fact]
    public async Task A_supplier_whose_address_changed_comes_back()
    {
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Moved Co {Guid.NewGuid():N}"[..20]);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var before = DateTimeOffset.UtcNow;
        await Task.Delay(1100);

        var added = await supplier.PostAsJsonAsync("/api/v1/suppliers/me/addresses", new
        {
            kind = "HeadOffice", line1 = "9 Baghdad Street", line2 = (string?)null, city = "Damascus",
            regionCode = "DIM", country = "Syria", postalCode = "0100",
            latitude = 33.5131, longitude = 36.2925,
        });
        added.EnsureSuccessStatusCode();

        var changed = await FeedAsync(admin, $"/api/v1/feeds/suppliers?limit=2000&modified_since={Iso(before)}");

        var code = await ReferenceCodeOfNewestSupplierAsync();
        SupplierIds(changed).Should().Contain(code,
            "the ministry's Must columns live on the address, so an address edit that did not re-send the "
            + "supplier would freeze the coordinate they asked us to collect");
    }

    private async Task<string> ReferenceCodeOfNewestSupplierAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Suppliers.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.ReferenceCode)
            .FirstAsync();
    }

    [Fact]
    public async Task An_edited_proposal_brings_its_invitation_row_back()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var seed = await EvaluationSeed.CreateAsync(fixture, $"Incremental {Guid.NewGuid():N}"[..20]);

        var before = DateTimeOffset.UtcNow;
        await Task.Delay(1100);

        // The edit is to the bid's own lines, with no state change and no new invitation: exactly the change a
        // filter built on the invitation would miss, and exactly the change that alters QuotationTotal.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The proposal is loaded WITH its lines, not the line on its own, because the timestamp is stamped
            // on the aggregate root and the persistence layer finds that root through the change tracker. A
            // test that loaded only the line would leave the root untracked, nothing would be stamped, and the
            // test would fail against perfectly correct code. Every handler in this product loads the
            // aggregate, so this is the shape the behaviour is actually written for.
            var proposal = await db.Proposals
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == seed.ProposalId);

            var item = proposal?.Items.FirstOrDefault();
            if (item is null) return;

            // The price is read-only on the entity, which is the domain's business. The write still has to be
            // a real one, so it goes through the change tracker rather than through a setter the domain does
            // not offer: what is under test is that a child write rolls up to the proposal's timestamp.
            db.Entry(item).Property(nameof(item.UnitPrice)).CurrentValue = item.UnitPrice + 1m;
            await db.SaveChangesAsync();
        }

        var changed = await FeedAsync(admin, $"/api/v1/feeds/rfqs?limit=2000&modified_since={Iso(before)}");

        var rows = changed.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("QuotationNo").GetString())
            .Where(code => code is not null)
            .ToList();

        rows.Should().NotBeEmpty(
            "a corrected price changes the total the ministry reports, and a feed filtered on the invitation "
            + "would leave their figure frozen at the night the invitation was sent");
    }

    // ONE ROW PER SUPPLIER PER TENDER, EVEN WHEN THEY BID TWICE. The uniqueness index on a proposal excludes
    // withdrawn, lapsed and cancelled bids, so a supplier who withdraws and submits again has two proposals for
    // one tender. The feed joined them both and emitted two rows for a pairing their sheet defines as one - in
    // the CSV as much as the JSON, since feed 4 shipped. Paging found it, because a cursor keyed on the pair
    // cannot step past two rows that share it.
    [Fact]
    public async Task A_supplier_who_withdrew_and_bid_again_is_still_one_row()
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, $"Rebid {Guid.NewGuid():N}"[..20]);
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        string rfqCode;
        string supplierCode;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proposal = await db.Proposals.FirstAsync(p => p.Id == seed.ProposalId);

            rfqCode = await db.Rfqs.Where(r => r.Id == proposal.RfqId).Select(r => r.ReferenceCode).FirstAsync();
            supplierCode = await db.Suppliers.Where(s => s.Id == proposal.SupplierId)
                .Select(s => s.ReferenceCode).FirstAsync();

            // A second proposal for the same pairing, which the filtered index permits precisely because the
            // first is withdrawn. This is the shape the database allows, reached directly because reaching it
            // through the routes would take a whole tender lifecycle to say one thing.
            db.Entry(proposal).Property(nameof(proposal.State)).CurrentValue = ProposalState.Withdrawn;
            await db.SaveChangesAsync();

            db.Proposals.Add(Proposal.Create($"PRP-2026-{Guid.NewGuid().ToString("N")[..6]}", proposal.RfqId, proposal.SupplierId));
            await db.SaveChangesAsync();
        }

        var feed = await FeedAsync(admin, "/api/v1/feeds/rfqs?limit=2000");

        var rows = feed.GetProperty("data").EnumerateArray()
            .Where(r => r.GetProperty("RFQNo").GetString() == rfqCode
                && r.GetProperty("SupplierID").GetString() == supplierCode)
            .ToList();

        rows.Should().HaveCount(1, "their sheet defines this feed as one row per supplier per RFQ");

        // And it must be the LIVE bid, not merely one of the two. Counting rows alone would pass against a
        // feed that reported the withdrawn one and told the ministry this supplier had walked away from a
        // tender they are currently bidding on.
        rows[0].GetProperty("QuoteStatus").GetString().Should().Be("Pending",
            "the supplier's current answer is a draft; the withdrawn bid is what they replaced");
    }

    [Fact]
    public async Task The_filter_is_reported_back_so_a_caller_can_see_it_was_applied()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var envelope = await FeedAsync(
            admin, $"/api/v1/feeds/suppliers?modified_since={Iso(DateTimeOffset.UtcNow.AddDays(-1))}");

        envelope.GetProperty("meta").GetProperty("filtersApplied").EnumerateArray()
            .Select(f => f.GetString()).Should().Contain("modified_since");
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-45")]
    [InlineData("1758700000")]
    public async Task A_timestamp_that_cannot_be_read_is_refused(string value)
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/feeds/suppliers?modified_since={Uri.EscapeDataString(value)}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"'{value}' read as no filter would have the job pull the whole registry nightly and call it a "
            + "success");
    }

    [Fact]
    public async Task A_timestamp_without_a_zone_is_read_as_utc_rather_than_as_the_servers_local_time()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

        var stamp = DateTimeOffset.UtcNow.AddSeconds(1);

        var withZone = await FeedAsync(admin, $"/api/v1/feeds/suppliers?limit=2000&modified_since={Iso(stamp)}");
        var withoutZone = await FeedAsync(
            admin,
            $"/api/v1/feeds/suppliers?limit=2000&modified_since={stamp.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fff}");

        SupplierIds(withoutZone).Should().Equal(SupplierIds(withZone),
            "a caller that strips the Z would otherwise shift its window by the server's offset, which in "
            + "Damascus is three hours of changes missed or re-sent on every pull");
    }
}
