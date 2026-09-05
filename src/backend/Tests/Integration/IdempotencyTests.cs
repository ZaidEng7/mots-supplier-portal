using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Idempotency;
using MotsSupplierPortal.Infrastructure.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-053/§8.2, clause by clause. The one that matters is 3: <i>"a supplier double-clicking Submit
/// Proposal cannot create two proposals"</i>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IdempotencyTests(PostgresApiFixture fixture)
{
    /// <summary>
    /// A DRAFT proposal on an RFQ whose submission window is open, plus the raw client that controls
    /// its own headers.
    ///
    /// <para>Deliberately not EvaluationSeed: that helper drives the RFQ to UnderEvaluation and moves
    /// its proposal to UnderReview, so submit answers 409 on the state machine before idempotency is
    /// reached. The first version of this suite used it and every test failed on that 409 - which is a
    /// setup error, not a finding.</para>
    /// </summary>
    private async Task<(HttpClient Raw, string ProposalCode)> ReadyToSubmitAsync(string label)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var template = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Tpl {Guid.NewGuid():N}" });
        var templateId = (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = $"{label} RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddMinutes(5),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(7),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var rfqCode = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{rfqCode}/evaluation-template", new { evaluationTemplateId = templateId });

        var (supplier, supplierId) = await ActiveSupplierAsync($"{label} {Guid.NewGuid():N}"[..30]);
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{rfqCode}/invitations", new { supplierId });
        await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{rfqCode}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{rfqCode}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Open the window in STORAGE and let the real job make the transition - no sleeping, no race
        // (the lesson from CrossOrganizationScopeTests turning main red).
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rfqs.Where(r => r.ReferenceCode == rfqCode).ExecuteUpdateAsync(p => p
                .SetProperty(r => r.SubmissionOpensAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await using (var jobScope = fixture.Services.CreateAsyncScope())
        {
            await jobScope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        var started = await supplier.PostAsync($"/api/v1/rfqs/{rfqCode}/proposals", null);
        started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());
        var code = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposalCode").GetString()!;

        await ProposalPatch.PriceItemAsync(supplier, code, itemId, 10m, 5m);
        await ProposalPatch.SetTermsAsync(supplier, code, new
        {
            currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
            validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
            validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
        });

        var raw = fixture.CreateRawClient();
        raw.DefaultRequestHeaders.Authorization = supplier.DefaultRequestHeaders.Authorization;
        return (raw, code);
    }

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, MotsSupplierPortal.Domain.Suppliers.SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, MotsSupplierPortal.Domain.Suppliers.SupplierLifecycleState.Active));
        return (client, supplier.Id);
    }

    private static HttpRequestMessage Submit(string proposalCode, string key, string etag) =>
        new(HttpMethod.Post, $"/api/v1/proposals/{proposalCode}/submit")
        {
            Headers = { { "Idempotency-Key", key }, { "If-Match", etag } },
        };

    private async Task<string> ETagAsync(HttpClient raw, string proposalCode) =>
        (await raw.GetAsync($"/api/v1/proposals/{proposalCode}")).Headers.ETag!.Tag;

    [Fact]
    public async Task A_double_clicked_submit_is_processed_once_and_replayed_the_second_time()
    {
        var (raw, code) = await ReadyToSubmitAsync("Idem Submit");
        var etag = await ETagAsync(raw, code);
        var key = Guid.NewGuid().ToString();

        // §8.2.2: first call processed normally.
        var first = await raw.SendAsync(Submit(code, key, etag));
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstBody = await first.Content.ReadAsStringAsync();

        // §8.2.3: retry with the same key and the same fingerprint replays verbatim, with the header.
        var second = await raw.SendAsync(Submit(code, key, etag));

        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "the stored response is replayed, so the second click does not meet the state guard's 409");
        second.Headers.TryGetValues("Idempotency-Replayed", out var flag).Should().BeTrue();
        flag!.Should().ContainSingle().Which.Should().Be("true");
        (await second.Content.ReadAsStringAsync()).Should().Be(firstBody, "replayed verbatim, per §8.2.3");

        // And it happened once. Asserted against storage, which is the claim that matters - the two
        // matching responses above would also be produced by a handler that ran twice idempotently.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audits = await db.AuditLogs.CountAsync(a => a.ReferenceCode == code && a.Action == "proposal_submitted");
        audits.Should().Be(1, "the transition was recorded exactly once");
    }

    [Fact]
    public async Task Without_the_replay_the_second_click_would_have_been_refused()
    {
        // The control for the test above. Without idempotency a double-click meets the state guard and
        // gets a 409 - the work is not duplicated, but the client is told its own retry failed. That is
        // the behaviour §8.2 exists to improve on, and proving it still happens under a DIFFERENT key
        // is what shows the replay above came from the key rather than from the state machine.
        var (raw, code) = await ReadyToSubmitAsync("Idem Control");
        var etag = await ETagAsync(raw, code);

        (await raw.SendAsync(Submit(code, Guid.NewGuid().ToString(), etag)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var freshEtag = await ETagAsync(raw, code);
        var refused = await raw.SendAsync(Submit(code, Guid.NewGuid().ToString(), freshEtag));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a new key is a new intent, so the state guard answers - and it refuses, because the " +
            "proposal is no longer a draft");
    }

    [Fact]
    public async Task A_missing_key_is_refused_with_428_on_a_transition_that_requires_one()
    {
        var (raw, code) = await ReadyToSubmitAsync("Idem Missing");
        var etag = await ETagAsync(raw, code);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/proposals/{code}/submit")
        {
            Headers = { { "If-Match", etag } },
        };
        var response = await raw.SendAsync(request);

        // §8.2: "a missing key on these returns 428 (IDEMPOTENCY_KEY_REQUIRED)".
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REQUIRED");

        // And nothing happened - a refused precondition must not have submitted anything.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Proposals.AsNoTracking().Where(p => p.ReferenceCode == code)
            .Select(p => p.State).FirstAsync()).ToString().Should().Be("Draft");
    }

    [Fact]
    public async Task The_same_key_on_a_different_request_is_refused_rather_than_answered()
    {
        // §8.2.4: same key, different fingerprint -> 409. Replaying here would hand a client the
        // outcome of a call it never made, which is worse than any duplicate.
        //
        // Two DIFFERENT proposals belonging to the same supplier, because the fingerprint covers the
        // path. The first version of this test reused the key on /withdraw and got a 200 - correctly:
        // withdraw does not declare the filter. §8.2 says every non-idempotent POST "accepts" the
        // header, and today only the three it names as REQUIRED honour it. Recorded in the backlog
        // rather than papered over by widening the test's expectation.
        var (raw, code) = await ReadyToSubmitAsync("Idem Reuse A");
        var (rawOther, otherCode) = await ReadyToSubmitAsync("Idem Reuse B");
        rawOther.Dispose();

        var key = Guid.NewGuid().ToString();
        (await raw.SendAsync(Submit(code, key, await ETagAsync(raw, code)))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Same caller, same key, a different path - so a different fingerprint. The 404 that would
        // follow on scope is beside the point: the filter refuses before the handler is reached.
        var elsewhere = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/proposals/{otherCode}/submit")
        {
            Headers = { { "Idempotency-Key", key }, { "If-Match", "\"AAAAAQ\"" } },
        };
        var response = await raw.SendAsync(elsewhere);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task One_callers_key_cannot_replay_another_callers_response()
    {
        // The record is keyed (UserId, Key), not Key alone. The key is client-generated, so two
        // suppliers picking the same UUID is trivial on purpose - and a global key space would let one
        // caller read another's response, which is a disclosure rather than a duplicate.
        var (rawA, codeA) = await ReadyToSubmitAsync("Idem Tenant A");
        var (rawB, codeB) = await ReadyToSubmitAsync("Idem Tenant B");

        var sharedKey = Guid.NewGuid().ToString();

        (await rawA.SendAsync(Submit(codeA, sharedKey, await ETagAsync(rawA, codeA))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Supplier B uses the SAME key on their own submit. It must be processed, not replayed.
        var bResponse = await rawB.SendAsync(Submit(codeB, sharedKey, await ETagAsync(rawB, codeB)));

        bResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        bResponse.Headers.Contains("Idempotency-Replayed").Should().BeFalse(
            "B's request is B's own intent - replaying A's response here would disclose it");
        (await bResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("proposalCode").GetString().Should().Be(codeB);
    }

    [Fact]
    public async Task The_cleanup_job_removes_expired_records_and_keeps_live_ones()
    {
        var (raw, code) = await ReadyToSubmitAsync("Idem Cleanup");
        (await raw.SendAsync(Submit(code, Guid.NewGuid().ToString(), await ETagAsync(raw, code))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.IdempotencyRecords.CountAsync()).Should().BeGreaterThan(0, "control: there is something to keep");

            // Age one record past its window, leaving the rest live. Both directions in one run.
            var oldest = await db.IdempotencyRecords.OrderBy(r => r.CreatedAt).FirstAsync();
            await db.IdempotencyRecords.Where(r => r.Id == oldest.Id)
                .ExecuteUpdateAsync(p => p.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdempotencyCleanupJob>().RunAsync(CancellationToken.None);
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.IdempotencyRecords.AnyAsync(r => r.ExpiresAt < DateTimeOffset.UtcNow))
                .Should().BeFalse("every expired record is gone");
            (await db.IdempotencyRecords.CountAsync()).Should().BeGreaterThan(0,
                "and the unexpired ones are still there - a job that deleted everything would also pass the line above");
        }
    }
}
