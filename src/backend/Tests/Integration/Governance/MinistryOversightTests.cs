// SCR-601, 602, 603 and 606 - the four Ministry screens, under D-66.
//
// These tests are the record of a disclosure, not only of a feature. BRULE-087's default is aggregate-only, and
// these four screens are the exception: named tenders, named suppliers, named bidders and their numbers. D-57
// relayed that the Ministry may see commercial figures and required written sign-off first; D-66 records that
// they shipped without it, at the product owner's direction, at the widest of the four scopes offered - live
// tenders included, per-bidder values shown.
//
// So the load-bearing test here is not "the screen returns rows". It is the one that reads a rival's bid on a
// tender that is still open, because that is the property somebody will need to check when they ask what exactly
// was disclosed. Under the narrower scope D-57 offered, that list would be empty until the tender was decided.
// The flag is turned on inside the test rather than assumed, because it ships OFF everywhere except the
// demonstration seed and a test relying on the shipped state would be asserting the demo environment's
// configuration rather than this screen's behaviour. The tender's own row carries the buying body by name, which
// no aggregate read ever did.
//
// The first test is the gate D-57 asks for, asserted where it can be: this fixture runs with DevSeed disabled,
// which is every environment that is not the demonstration one, and the flag must be OFF there. It was a
// migration first and that was wrong - a migration runs everywhere, so the step that creates the schema in
// production would have switched the disclosure on there too. The approval it rests on is bounded to
// demonstration data, and a mechanism that ignores the boundary makes the approval mean something it does not
// say. The switch now lives in DevDataSeeder, behind the same gate as the demo accounts, and that seeder refuses
// to run outside Development.
//
// A draft bid is not disclosed, which is the one line this widening does NOT cross: a draft has been offered to
// nobody, and no reading of D-57 covers a supplier's unfinished thinking. The draft is written in storage rather
// than through the API, because by the time the seed has opened evaluation the submission window is closed and
// no route can produce a draft on this tender any more - what is being tested is the read's predicate, not how
// the row got there. It uses a supplier with no bid on THIS tender, because unique(rfq_id, supplier_id) is what
// stops one supplier holding two and reusing the seed's bidder would collide with the bid the test needs to
// survive.
//
// The tender monitor lists across organizations and refuses an unknown state, which is §6.2's silent-widening
// case: Enum.TryParse simply fails on a typo, no predicate is applied, and the screen shows every tender inside
// a list it has labelled with one state.
//
// The supplier registry and the award analytics answer, with the count being an aggregate BRULE-086 always
// granted while the value is what the flag governs - so with the flag on it must be a number rather than null.
//
// Nobody but the governance persona may read any of the four.
//
// With the flag off the values are null and the counts remain: the flag is still the control D-6 built it to be,
// so the oversight remains and the money does not. That is the state the product ships in outside the
// demonstration seed, so nothing in that test has to switch anything - it asserts the default.

namespace MotsSupplierPortal.Tests.Integration.Governance;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinistryOversightTests(PostgresApiFixture fixture)
{
    private const string Rfqs = "/api/v1/ministry/rfqs";
    private const string Suppliers = "/api/v1/ministry/suppliers";
    private const string Awards = "/api/v1/ministry/awards";

    private async Task<bool> CommercialValuesOnAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SupplierFieldConfigs.AsNoTracking()
            .Where(c => c.FieldCode == "commercialValues")
            .Select(c => c.IsEnabled)
            .FirstAsync();
    }

    [Fact]
    public async Task The_commercial_visibility_flag_is_off_unless_the_demonstration_data_is_seeded()
    {
        (await CommercialValuesOnAsync()).Should().BeFalse(
            "D-6/BRULE-087's default is withhold, and enabling it anywhere real needs the written sign-off "
            + "D-57 names - a person, a date and the scope");
    }

    [Fact]
    public async Task A_ministry_viewer_reads_a_rivals_bid_on_a_tender_that_is_still_open()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MinistryLive");
        await SetCommercialValuesAsync(true);

        try
        {
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var detail = await ministry.GetFromJsonAsync<JsonElement>($"{Rfqs}/{seeded.RfqCode}");

        detail.GetProperty("commercialValuesVisible").GetBoolean().Should().BeTrue();

        var bids = detail.GetProperty("bids").EnumerateArray().ToList();
        bids.Should().NotBeEmpty("the seed submits one bid");

        var bid = bids[0];
        bid.GetProperty("supplierDisplayNameEn").GetString().Should().NotBeNullOrWhiteSpace(
            "the bidder is NAMED - A-8 anonymises bidders for the evaluators themselves, and this screen "
            + "deliberately does not");
        bid.GetProperty("totalValue").GetDecimal().Should().BeGreaterThan(0m,
            "and their number is shown, which is the whole of what D-66 decided");

        detail.GetProperty("summary").GetProperty("organizationNameEn").GetString()
            .Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await SetCommercialValuesAsync(false);
        }
    }

    [Fact]
    public async Task A_draft_bid_is_not_disclosed()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MinistryDraft");

        string draftCode;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rfqId = await db.Rfqs.Where(r => r.ReferenceCode == seeded.RfqCode).Select(r => r.Id).FirstAsync();
            var supplierId = await db.Suppliers
                .Where(s => !db.Proposals.Any(p => p.RfqId == rfqId && p.SupplierId == s.Id))
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.Id)
                .FirstAsync();

            draftCode = $"PRP-DRAFT-{Guid.NewGuid().ToString("N")[..6]}";
            db.Proposals.Add(MotsSupplierPortal.Domain.Proposals.Proposal.Create(draftCode, rfqId, supplierId));
            await db.SaveChangesAsync();
        }

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        var detail = await ministry.GetFromJsonAsync<JsonElement>($"{Rfqs}/{seeded.RfqCode}");

        detail.GetProperty("bids").EnumerateArray().Select(b => b.GetProperty("proposalCode").GetString())
            .Should().NotContain(draftCode, "a draft bid is not a bid anybody has made");
        detail.GetProperty("bids").EnumerateArray().Should().NotBeEmpty(
            "and the control: the SUBMITTED bid is still there, so the predicate excludes drafts rather than "
            + "excluding everything");
    }

    [Fact]
    public async Task The_tender_monitor_lists_across_organizations_and_refuses_an_unknown_state()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MinistryList");
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

        var body = await ministry.GetFromJsonAsync<JsonElement>($"{Rfqs}?pageSize=100");
        var codes = body.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).ToList();

        codes.Should().Contain(seeded.RfqCode,
            "the Ministry crosses organizations by design - this read has no organization predicate, which "
            + "is why governance.read is its own permission");

        var row = body.GetProperty("data").EnumerateArray()
            .First(r => r.GetProperty("referenceCode").GetString() == seeded.RfqCode);
        row.GetProperty("submittedProposals").GetInt32().Should().BeGreaterThan(0);
        row.GetProperty("invitedSuppliers").GetInt32().Should().BeGreaterThan(0);

        var refused = await ministry.GetAsync($"{Rfqs}?state=Publishd");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("INVALID_FILTER_VALUE");
    }

    [Fact]
    public async Task The_supplier_registry_and_the_award_analytics_answer()
    {
        await EvaluationSeed.CreateAsync(fixture, "MinistryReg");
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

        var suppliers = await ministry.GetFromJsonAsync<JsonElement>($"{Suppliers}?pageSize=50");
        suppliers.GetProperty("data").EnumerateArray().Should().NotBeEmpty();
        var supplier = suppliers.GetProperty("data").EnumerateArray().First();
        supplier.GetProperty("supplierCode").GetString().Should().NotBeNullOrWhiteSpace();
        supplier.GetProperty("awardsWon").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        supplier.TryGetProperty("awardedValue", out var awardedValue).Should().BeTrue();
        awardedValue.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        await SetCommercialValuesAsync(true);
        try
        {
            var analytics = await ministry.GetFromJsonAsync<JsonElement>(Awards);
            analytics.GetProperty("commercialValuesVisible").GetBoolean().Should().BeTrue();
            analytics.GetProperty("totalAwards").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            analytics.GetProperty("totalAwardedValue").ValueKind.Should().Be(JsonValueKind.Number);
        }
        finally
        {
            await SetCommercialValuesAsync(false);
        }
    }

    [Fact]
    public async Task Nobody_but_the_governance_persona_may_read_any_of_the_four()
    {
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager);
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"MinOut {Guid.NewGuid():N}"[..20]);

        foreach (var route in new[] { Rfqs, Suppliers, Awards, $"{Rfqs}/RFQ-2026-000001" })
        {
            (await officer.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
            (await manager.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
            (await supplier.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"a supplier reading {route} would be reading its competitors' bids");
        }

        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);
        (await ministry.GetAsync(Rfqs)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ministry.GetAsync(Suppliers)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ministry.GetAsync(Awards)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ministry.GetAsync($"{Rfqs}/does-not-exist")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown tender is a 404, not an empty detail page");
    }

    [Fact]
    public async Task With_the_flag_off_the_values_are_null_and_the_counts_remain()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MinistryFlagOff");
        var ministry = await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer);

        {
            var detail = await ministry.GetFromJsonAsync<JsonElement>($"{Rfqs}/{seeded.RfqCode}");

            detail.GetProperty("commercialValuesVisible").GetBoolean().Should().BeFalse();
            var bid = detail.GetProperty("bids").EnumerateArray().First();
            bid.GetProperty("totalValue").ValueKind.Should().Be(JsonValueKind.Null,
                "null is not zero: policy withholding a figure and a bid of nothing are different facts");
            bid.GetProperty("supplierDisplayNameEn").GetString().Should().NotBeNullOrWhiteSpace(
                "the flag governs the MONEY. Who bid is identity, and this screen's whole grant is that the "
                + "Ministry may see it");

            var analytics = await ministry.GetFromJsonAsync<JsonElement>(Awards);
            analytics.GetProperty("totalAwardedValue").ValueKind.Should().Be(JsonValueKind.Null);
            analytics.GetProperty("totalAwards").GetInt32().Should().BeGreaterThanOrEqualTo(0,
                "the counts survive: they are the aggregate grant BRULE-086 gave outright");
        }
    }

    private async Task SetCommercialValuesAsync(bool enabled)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SupplierFieldConfigs.Where(c => c.FieldCode == "commercialValues")
            .ExecuteUpdateAsync(p => p.SetProperty(c => c.IsEnabled, enabled));
    }
}
