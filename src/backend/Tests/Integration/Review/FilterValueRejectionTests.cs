// An unrecognised filter VALUE must not widen the result set. Two surfaces, in this order: the supplier
// documents list filtered by DocumentState, then the review queue filtered by state and by assignedTo.
//
// The regression this closes. API-ARCHITECTURE.md §6.2 rules on an unknown filter KEY and is silent on an
// unknown value, so dropping the value was a defensible reading of the document. It is not a defensible
// behaviour: dropping the only member leaves an EMPTY filter, and an empty filter returns everything.
// ?state=Approvd - one transposed letter - answers with the unfiltered set while reading as a working filtered
// list. Identical in shape to Batch 0.2's ?aggregateTyp=X, which returned the entire audit trail for a typo.
// The review queue's version is worse still: an unrecognised state there did not merely empty the filter, it
// fell through to the DEFAULT three-state set.
//
// The second test in each pair is the dangerous one. A filter with one good and one bad member failing is nice;
// a filter with ONLY a bad member is where silence becomes "everything", so that case asserts explicitly that
// the unfiltered set is not returned - and proves the set it would have returned is genuinely non-empty, so the
// assertion is about rejection rather than about there being nothing to leak.
//
// DOCUMENTS. One valid and one invalid state is rejected; a single invalid state does not return the unfiltered
// set; a valid state still filters. Case matters, because accepting "pendingscan" would make the filter's
// vocabulary wider than the vocabulary of the responses it filters, and this API emits enum names as written.
//
// THE REVIEW QUEUE, which filters by a named SUBSET of SupplierOnboardingState. A single invalid state does not
// return the default set, and one valid plus one invalid is rejected. The subtle case is the reason
// Enum.TryParse alone would not be enough here: ProfileInProgress IS a real SupplierOnboardingState member but
// is NOT one the review queue filters by, so a whole-enum check would accept it and then fall through to the
// default three-state set - widening, which is the exact defect this guard closes. That test used to assert the
// same of Approved and no longer does: F-6 made Approved and Rejected filterable on purpose, because a decided
// application dropped out of every list the moment it was decided and a reviewer could not reach a decision they
// had made. The vocabulary grew by two deliberate members; the property being protected - a real enum member
// that is not in the vocabulary must be REFUSED rather than silently widening - is unchanged, and is asserted
// against a member that is still outside it. The two members the vocabulary deliberately gained are asserted
// too, so that removing them again is a decision somebody has to make rather than a regression nobody notices.
//
// ?assignedTo takes two literals or a reviewer id. Its dangerous case is a value that is neither literal nor a
// parseable id, where the handler's if/else chain applied NO predicate and the whole queue came back; the
// non-empty control is what stops "did not return everything" passing because there was nothing to return. A
// malformed id is an invalid VALUE rather than an absent filter, which is the distinction the old chain
// silently collapsed. The control is that all three legitimate forms still work - without it the fix could have
// been "reject everything", which passes every negative above - and "unassigned" must still FILTER rather than
// merely return 200, so the freshly queued supplier in that test has no reviewer, putting it in the unassigned
// set and absent from any specific reviewer's set.

namespace MotsSupplierPortal.Tests.Integration.Review;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class FilterValueRejectionTests(PostgresApiFixture fixture)
{
    private async Task<(HttpClient Reviewer, string SupplierCode)> SeededSupplierAsync(int documents)
    {
        var name = $"FilterVal {Guid.NewGuid():N}"[..26];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        string code;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
            code = supplier.ReferenceCode;
            var type = await db.DocumentTypes.Where(t => t.IsActive && !t.ExpiryTracked).FirstAsync();

            for (var i = 0; i < documents; i++)
            {
                db.SupplierDocuments.Add(SupplierDocument.CreatePendingScan(
                    $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                    supplier.Id, type.Id, version: i + 1, quarantineKey: $"fv/{Guid.NewGuid():N}",
                    originalFileName: $"d{i}.pdf", contentType: "application/pdf", sizeBytes: 64,
                    uploadedByUserId: Guid.CreateVersion7(), issueDate: null, expiryDate: null,
                    expiryTracked: false, today: DateOnly.FromDateTime(DateTime.UtcNow)));
            }
            await db.SaveChangesAsync();
        }

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        return (reviewer, code);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "silently narrowing or widening is the failure; the caller has to be told");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("INVALID_FILTER_VALUE");
        problem.GetProperty("type").GetString().Should().Be("https://api.mots-portal.sy/errors/validation",
            "§7.1 has no slug for a bad filter value, so the documented validation slug is reused " +
            "rather than one being invented");
        problem.GetProperty("errors").EnumerateArray().Single()
            .GetProperty("field").GetString().Should().Be(field);
    }

    [Fact]
    public async Task Documents_one_valid_and_one_invalid_state_is_rejected()
    {
        var (reviewer, code) = await SeededSupplierAsync(3);

        var response = await reviewer.GetAsync($"/api/v1/suppliers/{code}/documents?state=PendingScan,Approvd");

        await AssertRejectedAsync(response, "state");
    }

    [Fact]
    public async Task Documents_a_single_invalid_state_does_not_return_the_unfiltered_set()
    {
        var (reviewer, code) = await SeededSupplierAsync(3);

        var response = await reviewer.GetAsync($"/api/v1/suppliers/{code}/documents?state=Approvd");

        await AssertRejectedAsync(response, "state");

        var unfiltered = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents");
        unfiltered.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Documents_a_valid_state_still_filters()
    {
        var (reviewer, code) = await SeededSupplierAsync(3);

        var pending = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?state=PendingScan");
        var approved = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers/{code}/documents?state=Approved");

        pending.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(3);
        approved.GetProperty("pagination").GetProperty("totalCount").GetInt32().Should().Be(0,
            "control: a VALID value that matches nothing returns an empty set - which is exactly " +
            "what an invalid value must not be allowed to look like");
    }

    [Fact]
    public async Task Documents_a_wrongly_cased_state_is_rejected()
    {
        var (reviewer, code) = await SeededSupplierAsync(1);

        await AssertRejectedAsync(await reviewer.GetAsync($"/api/v1/suppliers/{code}/documents?state=pendingscan"), "state");
    }

    [Fact]
    public async Task Review_queue_a_single_invalid_state_does_not_return_the_default_set()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        var response = await reviewer.GetAsync("/api/v1/review/queue?state=Approvd");

        await AssertRejectedAsync(response, "state");
    }

    [Fact]
    public async Task Review_queue_one_valid_and_one_invalid_state_is_rejected()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        await AssertRejectedAsync(await reviewer.GetAsync("/api/v1/review/queue?state=Submitted,Approvd"), "state");
    }

    [Fact]
    public async Task Review_queue_rejects_a_real_enum_member_that_is_not_a_queue_filter()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        await AssertRejectedAsync(await reviewer.GetAsync("/api/v1/review/queue?state=ProfileInProgress"), "state");
    }

    [Fact]
    public async Task Review_queue_accepts_the_two_decided_states()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        (await reviewer.GetAsync("/api/v1/review/queue?state=Approved")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reviewer.GetAsync("/api/v1/review/queue?state=Rejected")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Review_queue_a_valid_state_still_works()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        (await reviewer.GetAsync("/api/v1/review/queue?state=UnderReview")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: the accepted vocabulary must still be accepted");
    }

    [Fact]
    public async Task Review_queue_an_invalid_assignedTo_does_not_return_the_unfiltered_queue()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        await SeedQueuedSupplierAsync();

        var response = await reviewer.GetAsync("/api/v1/review/queue?assignedTo=grbage");

        await AssertRejectedAsync(response, "assignedTo");

        var unfiltered = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?pageSize=100");
        unfiltered.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0,
            "the queue this must not have returned is genuinely non-empty, or the rejection above " +
            "proves nothing about widening");
    }

    [Fact]
    public async Task Review_queue_a_malformed_guid_assignedTo_is_rejected()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);

        await AssertRejectedAsync(
            await reviewer.GetAsync("/api/v1/review/queue?assignedTo=01a06349-not-a-guid"), "assignedTo");
    }

    [Theory]
    [InlineData("me")]
    [InlineData("unassigned")]
    [InlineData("01a06349-0d05-7992-9594-b3174d771ea5")]
    public async Task Review_queue_the_three_valid_assignedTo_forms_still_work(string assignedTo)
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        await SeedQueuedSupplierAsync();

        var response = await reviewer.GetAsync($"/api/v1/review/queue?assignedTo={Uri.EscapeDataString(assignedTo)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Review_queue_unassigned_and_a_reviewer_id_return_different_sets()
    {
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer, organizationId: null);
        var code = await SeedQueuedSupplierAsync();

        var unassigned = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue?assignedTo=unassigned&pageSize=100");
        var someoneElse = await reviewer.GetFromJsonAsync<JsonElement>(
            $"/api/v1/review/queue?assignedTo={Guid.CreateVersion7()}&pageSize=100");

        unassigned.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).Should().Contain(code);
        someoneElse.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("referenceCode").GetString()).Should().NotContain(code,
                "a reviewer id nobody holds must filter to nothing, not fall through to everything");
    }

    private async Task<string> SeedQueuedSupplierAsync()
    {
        var name = $"Queued {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Submitted)
            .SetProperty(s => s.AssignedReviewerId, (Guid?)null));
        return supplier.ReferenceCode;
    }
}
