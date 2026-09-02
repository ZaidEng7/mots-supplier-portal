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

/// <summary>FEAT-03.6/FR-ONB-012: reviewer claim/unassign and queue filtering by state and
/// assignee. [ASSUMPTION] (see Supplier.AssignedReviewerId's own doc comment): manual self-claim,
/// not round-robin or manager-assigned - there is no assignment model specified anywhere in the
/// product docs.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReviewQueueAssignmentTests(PostgresApiFixture fixture)
{
    private static Supplier MakeSubmitted(string tag)
    {
        var s = Supplier.Register(
            referenceCode: $"SUP-{tag}-{Guid.NewGuid():N}"[..20],
            displayNameAr: "شركة اختبار",
            displayNameEn: $"Assign Test {tag} {Guid.NewGuid():N}"[..40],
            registrationNumber: null,
            primaryRepresentativeName: "Tester",
            primaryRepresentativeEmail: $"{tag}-{Guid.NewGuid():N}@example.com",
            primaryRepresentativePhone: "+963900000000");
        s.MarkEmailVerified();
        s.UpdateCoreProfile(null, null, null, "USD");
        s.AddAddress(AddressKind.HeadOffice, "L1", null, "Damascus", "DM", "SY", null, null, null);
        s.LinkCategory("CAT-1", isComplianceCritical: false);
        s.AcceptTerms("v1");
        s.Submit([]);
        return s;
    }

    [Fact]
    public async Task Claiming_an_item_records_the_caller_as_assignee_and_is_audited()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        Supplier supplier;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplier = MakeSubmitted("CLAIM");
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
        }

        var response = await reviewer.PostAsync($"/api/v1/review/{supplier.ReferenceCode}/claim", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("assignedReviewerId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("assignedReviewerName").GetString().Should().NotBeNullOrEmpty();

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await verifyDb.AuditLogs
            .Where(a => a.AggregateType == "Supplier" && a.ReferenceCode == supplier.ReferenceCode && a.Action == "application_claimed")
            .FirstOrDefaultAsync();
        auditRow.Should().NotBeNull("claiming an item must be audited");
    }

    [Fact]
    public async Task Unassigning_releases_the_claim_and_is_audited()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        Supplier supplier;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplier = MakeSubmitted("UNASSIGN");
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
        }

        (await reviewer.PostAsync($"/api/v1/review/{supplier.ReferenceCode}/claim", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await reviewer.PostAsync($"/api/v1/review/{supplier.ReferenceCode}/unassign", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("assignedReviewerId").ValueKind.Should().Be(JsonValueKind.Null);

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await verifyDb.AuditLogs
            .Where(a => a.AggregateType == "Supplier" && a.ReferenceCode == supplier.ReferenceCode && a.Action == "application_unassigned")
            .FirstOrDefaultAsync();
        auditRow.Should().NotBeNull("unassigning must be audited too");
    }

    [Fact]
    public async Task Filtering_by_assignedTo_me_returns_only_the_caller_s_own_claims()
    {
        var reviewerA = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var reviewerB = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        Supplier claimedByA, claimedByB;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            claimedByA = MakeSubmitted("FILTA");
            claimedByB = MakeSubmitted("FILTB");
            db.Suppliers.AddRange(claimedByA, claimedByB);
            await db.SaveChangesAsync();
        }

        (await reviewerA.PostAsync($"/api/v1/review/{claimedByA.ReferenceCode}/claim", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reviewerB.PostAsync($"/api/v1/review/{claimedByB.ReferenceCode}/claim", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await reviewerA.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?assignedTo=me&pageSize=100");
        var codes = page.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("referenceCode").GetString()).ToList();

        codes.Should().Contain(claimedByA.ReferenceCode);
        codes.Should().NotContain(claimedByB.ReferenceCode, "assignedTo=me must resolve against the caller, not return every claimed item");
    }

    [Fact]
    public async Task Filtering_by_state_returns_only_that_state()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        Supplier submitted, underReview;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            submitted = MakeSubmitted("STATEFILT1");
            underReview = MakeSubmitted("STATEFILT2");
            underReview.PickUpForReview();
            db.Suppliers.AddRange(submitted, underReview);
            await db.SaveChangesAsync();
        }

        var page = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?state=UnderReview&pageSize=100");
        var codes = page.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("referenceCode").GetString()).ToList();

        codes.Should().Contain(underReview.ReferenceCode);
        codes.Should().NotContain(submitted.ReferenceCode, "state=UnderReview must exclude Submitted items");
    }

    [Fact]
    public async Task Filtering_by_unassigned_excludes_claimed_items()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        Supplier claimed, unclaimed;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            claimed = MakeSubmitted("UNCLAIMED1");
            unclaimed = MakeSubmitted("UNCLAIMED2");
            db.Suppliers.AddRange(claimed, unclaimed);
            await db.SaveChangesAsync();
        }

        (await reviewer.PostAsync($"/api/v1/review/{claimed.ReferenceCode}/claim", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?assignedTo=unassigned&pageSize=100");
        var codes = page.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("referenceCode").GetString()).ToList();

        codes.Should().Contain(unclaimed.ReferenceCode);
        codes.Should().NotContain(claimed.ReferenceCode);
    }
}
