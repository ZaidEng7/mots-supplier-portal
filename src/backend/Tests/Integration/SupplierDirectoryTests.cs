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
/// SCR-402 and SCR-307: the two reads across the supplier registry, neither of which existed.
///
/// <para><b>What was missing and what it cost.</b> A procurement officer looking for who could supply
/// something had to start from the OFFERING search - so a supplier with no catalogue entries was invisible
/// to the person deciding whom to invite. A reviewer had the queue, which holds only undecided cases, so an
/// approved supplier whose tax certificate expired last month appeared on no list at all: the only route to
/// them was typing their reference code into the address bar. T-099 recorded both, and both needed an
/// endpoint as well as a screen.</para>
///
/// <para><b>The two shapes are asserted as different.</b> A buyer must not read document history and a
/// reviewer must see states the buyer's list excludes, so a test that only checked "a list comes back"
/// would pass against one endpoint serving both - which is the design this deliberately does not have.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SupplierDirectoryTests(PostgresApiFixture fixture)
{
    private const string Directory = "/api/v1/supplier-directory";
    private const string Compliance = "/api/v1/review/suppliers";

    /// <summary>A supplier in a named onboarding state, with a display name this test can find again.</summary>
    private async Task<(string Code, Guid Id)> SeedSupplierAsync(
        string displayNameEn, SupplierOnboardingState onboarding, SupplierLifecycleState lifecycle,
        string? categoryCode = null)
    {
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, displayNameEn);

        Guid supplierId;
        string referenceCode;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.DisplayNameEn == displayNameEn);
            supplierId = supplier.Id;
            referenceCode = supplier.ReferenceCode;

            if (categoryCode is not null)
            {
                // Written in storage rather than through /me/category-links, which needs a signed-in supplier
                // client and an If-Match - and this test is about the directory's filter, not that route.
                db.CategoryLinks.Add(new CategoryLink { Id = Guid.CreateVersion7(), SupplierId = supplierId, CategoryCode = categoryCode });
                await db.SaveChangesAsync();
            }
        }

        // The states LAST, in their own scope, and this ordering is not cosmetic. Setting them first and then
        // calling SaveChanges for the category link in the same context wrote the tracked supplier's ORIGINAL
        // states back over the ExecuteUpdate - so the supplier this test had just approved was Draft again,
        // the directory correctly excluded it, and the failure read as a broken filter.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, onboarding)
                .SetProperty(s => s.LifecycleState, lifecycle));
        }

        return (referenceCode, supplierId);
    }

    private static List<string> CodesIn(JsonElement body) =>
        [.. body.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("supplierCode").GetString()!)];

    [Fact]
    public async Task The_buyer_directory_lists_approved_suppliers_and_not_applicants()
    {
        var unique = $"{Guid.NewGuid():N}"[..8];
        var approved = await SeedSupplierAsync($"Dir Approved {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active, "catering");
        var applicant = await SeedSupplierAsync($"Dir Submitted {unique}", SupplierOnboardingState.Submitted, SupplierLifecycleState.None);

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var body = await officer.GetFromJsonAsync<JsonElement>($"{Directory}?q={unique}&pageSize=50");

        var codes = CodesIn(body);
        codes.Should().Contain(approved.Code);
        codes.Should().NotContain(applicant.Code,
            "an invitation can only be sent to an approved supplier, so listing applicants would be a list "
            + "of companies the buyer cannot act on");

        var row = body.GetProperty("data").EnumerateArray()
            .First(d => d.GetProperty("supplierCode").GetString() == approved.Code);
        row.GetProperty("categoryCodes").EnumerateArray().Select(c => c.GetString()).Should().Contain("catering");
        row.GetProperty("lifecycleState").GetString().Should().Be("Active");

        // The buyer's shape, asserted as NOT carrying the reviewer's columns. Document health is the
        // reviewer's business, and a column nobody renders is still a disclosure.
        row.TryGetProperty("expiredDocumentCount", out _).Should().BeFalse();
        row.TryGetProperty("onboardingState", out _).Should().BeFalse(
            "every row would read Approved, and a constant column teaches a reader to stop looking at it");
    }

    [Fact]
    public async Task A_suspended_supplier_is_listed_rather_than_hidden()
    {
        // The decision this pins: a buyer who knows a company is registered must be able to find it. Hiding
        // a suspended supplier produces the other defect - absent, with no explanation anywhere.
        var unique = $"{Guid.NewGuid():N}"[..8];
        var suspended = await SeedSupplierAsync($"Dir Suspended {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Suspended);

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var all = await officer.GetFromJsonAsync<JsonElement>($"{Directory}?q={unique}&pageSize=50");
        CodesIn(all).Should().Contain(suspended.Code);
        all.GetProperty("data").EnumerateArray().First().GetProperty("lifecycleState").GetString()
            .Should().Be("Suspended", "the state is what tells the buyer not to bother inviting them");

        var activeOnly = await officer.GetFromJsonAsync<JsonElement>($"{Directory}?q={unique}&lifecycleState=Active&pageSize=50");
        CodesIn(activeOnly).Should().NotContain(suspended.Code);
    }

    [Fact]
    public async Task The_category_filter_narrows_and_an_unknown_filter_value_is_refused()
    {
        var unique = $"{Guid.NewGuid():N}"[..8];
        var caterer = await SeedSupplierAsync($"Dir Cater {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active, "catering");
        var hauler = await SeedSupplierAsync($"Dir Haul {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active, "transport");

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var catering = await officer.GetFromJsonAsync<JsonElement>($"{Directory}?q={unique}&category=catering&pageSize=50");
        CodesIn(catering).Should().Contain(caterer.Code).And.NotContain(hauler.Code);

        // §6.2's silent-widening case: the handler parses the lifecycle state with Enum.TryParse, so a typo
        // applies NO predicate and returns suspended suppliers inside a list the screen labels Active.
        var refused = await officer.GetAsync($"{Directory}?lifecycleState=Activ");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("INVALID_FILTER_VALUE");

        // And an unknown filter KEY, which is the same failure one level up: ?categry=catering would
        // otherwise return the unfiltered registry and read as a filtered view.
        (await officer.GetAsync($"{Directory}?categry=catering")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_directory_pages_by_name_and_a_hostile_cursor_returns_the_first_page()
    {
        var unique = $"{Guid.NewGuid():N}"[..8];
        await SeedSupplierAsync($"Dir Page A {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active);
        await SeedSupplierAsync($"Dir Page B {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active);

        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);

        var first = await officer.GetFromJsonAsync<JsonElement>($"{Directory}?q={unique}&pageSize=1&withCount=true");
        var firstCodes = CodesIn(first);
        firstCodes.Should().HaveCount(1);
        first.GetProperty("pagination").GetProperty("hasMore").GetBoolean().Should().BeTrue();
        first.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(2);

        var cursor = first.GetProperty("pagination").GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNull();

        var second = await officer.GetFromJsonAsync<JsonElement>(
            $"{Directory}?q={unique}&pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        CodesIn(second).Should().NotIntersectWith(firstCodes,
            "a cursor that repeated the row it ended on would make the second page a lie");

        // Same convention as every other cursor here: uninterpretable means page one, never a 500, and
        // never a token that reaches the database.
        var hostile = await officer.GetAsync($"{Directory}?q={unique}&cursor={Uri.EscapeDataString("'; DROP TABLE supplier.supplier; --")}");
        hostile.StatusCode.Should().Be(HttpStatusCode.OK);
        CodesIn(await hostile.Content.ReadFromJsonAsync<JsonElement>()).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_compliance_directory_carries_document_health_and_the_states_the_buyer_list_omits()
    {
        var unique = $"{Guid.NewGuid():N}"[..8];
        var rejected = await SeedSupplierAsync($"Comp Rejected {unique}", SupplierOnboardingState.Rejected, SupplierLifecycleState.None);
        var live = await SeedSupplierAsync($"Comp Live {unique}", SupplierOnboardingState.Approved, SupplierLifecycleState.Active);

        // An expired document on the approved supplier: the case the review queue cannot show, because the
        // queue holds undecided applications and this one was decided months ago.
        //
        // chamber_membership rather than a tax certificate, and the real expiry job rather than writing the
        // State column: the type matters because D-58 made the tax certificate award-critical, so expiring
        // one would ALSO suspend this supplier and the test would be asserting two rules at once.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var typeId = await db.DocumentTypes.Where(t => t.Code == "chamber_membership").Select(t => t.Id).FirstAsync();
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", live.Id, typeId, 1, "quarantine/key",
                $"expired-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);
            document.MarkScanClean("clean/key");
            document.Approve(Guid.CreateVersion7());
            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();

            // BRULE-020 refuses a past expiry at upload, so the only way to reach "approved, and has since
            // expired" - the ordinary passage of time - is to write the date the clock would have produced.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Infrastructure.Suppliers.DocumentExpiryJob>()
                .RunAsync(CancellationToken.None);
        }

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var body = await reviewer.GetFromJsonAsync<JsonElement>($"{Compliance}?q={unique}&pageSize=50");

        var codes = CodesIn(body);
        codes.Should().Contain(rejected.Code,
            "the reviewer's question includes who was rejected - the queue drops a case the moment it is decided");
        codes.Should().Contain(live.Code);

        var liveRow = body.GetProperty("data").EnumerateArray()
            .First(d => d.GetProperty("supplierCode").GetString() == live.Code);
        liveRow.GetProperty("expiredDocumentCount").GetInt32().Should().Be(1);
        liveRow.GetProperty("expiringDocumentCount").GetInt32().Should().Be(0,
            "expiring is a prompt and expired is a bar, and summing them would hide which one this row has");
        liveRow.GetProperty("onboardingState").GetString().Should().Be("Approved");

        var attention = await reviewer.GetFromJsonAsync<JsonElement>($"{Compliance}?q={unique}&documentHealth=attention&pageSize=50");
        CodesIn(attention).Should().Contain(live.Code).And.NotContain(rejected.Code,
            "the supplier with no documents at all has nothing needing attention");

        var ok = await reviewer.GetFromJsonAsync<JsonElement>($"{Compliance}?q={unique}&documentHealth=ok&pageSize=50");
        CodesIn(ok).Should().Contain(rejected.Code).And.NotContain(live.Code,
            "the two halves must be complements, or one of them is silently dropping rows");
    }

    [Fact]
    public async Task Each_directory_refuses_the_other_persona()
    {
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer);
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var evaluator = await StaffTestClient.CreateAsync(fixture, Roles.Evaluator);
        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Outsider {Guid.NewGuid():N}"[..20]);

        // The separation that makes two endpoints worth having: a buying officer has no business reading a
        // supplier's document history, and a reviewer is not choosing whom to invite.
        (await officer.GetAsync(Compliance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reviewer.GetAsync(Directory)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await evaluator.GetAsync(Directory)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await evaluator.GetAsync(Compliance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supplier.GetAsync(Directory)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a supplier must not be handed a list of its competitors");
        (await supplier.GetAsync(Compliance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The controls, so the refusals above are about permission rather than a broken route.
        (await officer.GetAsync(Directory)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reviewer.GetAsync(Compliance)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
