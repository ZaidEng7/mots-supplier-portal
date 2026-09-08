using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// P12 item 26's last two routes: a reviewer deciding two documents in a row, using only what the previous
/// response handed back.
///
/// <para><b>The defect this closes.</b> Both decisions demand an <c>If-Match</c> - two reviewers deciding one
/// document is the lost update worth refusing - and neither answered with a version. The SPA drops its cached
/// version the moment a mutation succeeds, because a kept version is stale by definition, so the SECOND
/// decision had nothing to send and met a 428. The reviewer's screen hid it by refetching after every
/// decision, which is a screen-by-screen habit rather than a property of the transport: any other client, and
/// any future screen that batches decisions, would have hit it.</para>
///
/// <para><b>What the ETag on these routes is.</b> The SUPPLIER's version, not the document's. A document is a
/// child and carries none, and the precondition's source - <c>GET /review/{referenceCode}</c> - issues the
/// supplier root's version, so that is the number the next <c>If-Match</c> needs. It rides on the header
/// rather than in the body, which stays the document §3 describes.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DocumentDecisionFreshETagTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_second_decision_can_use_the_ETag_the_first_returned()
    {
        var (supplierCode, first, second) = await SeedTwoDocumentsAwaitingReviewAsync();
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        // The ONLY read in this test. Everything after it lives on what the writes hand back, which is the
        // property under test - a client that refetches proves nothing here.
        var view = await reviewer.GetAsync($"/api/v1/review/{supplierCode}");
        view.IsSuccessStatusCode.Should().BeTrue(await view.Content.ReadAsStringAsync());
        view.Headers.ETag.Should().NotBeNull();

        var approved = await DecideAsync(reviewer, supplierCode, first, "approve", view.Headers.ETag!.ToString());
        approved.IsSuccessStatusCode.Should().BeTrue(await approved.Content.ReadAsStringAsync());
        approved.Headers.ETag.Should().NotBeNull(
            "P12 item 26: a guarded write that returns no version leaves the next one with nothing to send");
        approved.Headers.ETag!.ToString().Should().NotBe(view.Headers.ETag!.ToString(),
            "the aggregate moved, so the version must have moved with it - an unchanged ETag would be worse "
            + "than none, because the next write would send a value the row no longer has");

        var alsoApproved = await DecideAsync(
            reviewer, supplierCode, second, "approve", approved.Headers.ETag!.ToString());

        alsoApproved.IsSuccessStatusCode.Should().BeTrue(
            "this is the 428 the item names: before the fresh ETag, the second decision in a row could only "
            + "succeed by re-reading the supplier first. Body: " + await alsoApproved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_stale_version_is_still_refused()
    {
        // The control. A fresh ETag is only worth having if the guard it satisfies is still real: if the
        // precondition had quietly stopped being checked, the test above would pass for the wrong reason.
        var (supplierCode, first, second) = await SeedTwoDocumentsAwaitingReviewAsync();
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var view = await reviewer.GetAsync($"/api/v1/review/{supplierCode}");
        var stale = view.Headers.ETag!.ToString();

        (await DecideAsync(reviewer, supplierCode, first, "approve", stale))
            .IsSuccessStatusCode.Should().BeTrue();

        var replayed = await DecideAsync(reviewer, supplierCode, second, "approve", stale);

        replayed.StatusCode.Should().Be(System.Net.HttpStatusCode.PreconditionFailed,
            "the version the reviewer held before the first decision describes a row that has since moved");
    }

    [Fact]
    public async Task Rejecting_returns_one_too()
    {
        var (supplierCode, first, _) = await SeedTwoDocumentsAwaitingReviewAsync();
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);

        var view = await reviewer.GetAsync($"/api/v1/review/{supplierCode}");
        var rejected = await DecideAsync(
            reviewer, supplierCode, first, "reject", view.Headers.ETag!.ToString(),
            body: new { reason = "The scan is unreadable." });

        rejected.IsSuccessStatusCode.Should().BeTrue(await rejected.Content.ReadAsStringAsync());
        rejected.Headers.ETag.Should().NotBeNull("reject is the same aggregate write as approve");
    }

    private static async Task<HttpResponseMessage> DecideAsync(
        HttpClient reviewer, string supplierCode, string documentCode, string decision, string ifMatch,
        object? body = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/suppliers/{supplierCode}/documents/{documentCode}/{decision}");
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        return await reviewer.SendAsync(request);
    }

    /// <summary>
    /// One supplier holding two scanned-clean documents of DIFFERENT types, both awaiting a decision.
    ///
    /// <para>Two types rather than two versions of one: a superseded version is not pending review, so two
    /// versions of a single type would give the reviewer only one decision to make and the test would have no
    /// second write to try its returned ETag on.</para>
    /// </summary>
    private async Task<(string SupplierCode, string First, string Second)> SeedTwoDocumentsAwaitingReviewAsync()
    {
        var name = $"Fresh ETag {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = await db.Suppliers.Where(s => s.DisplayNameEn == name).FirstAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var codes = new List<string>();

        foreach (var typeCode in new[] { "commercial_registration", "tax_certificate" })
        {
            var typeId = await db.DocumentTypes.Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
            var document = SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplier.Id, typeId, 1, "quarantine/key",
                $"{typeCode}-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                issueDate: null, expiryDate: today.AddYears(1), expiryTracked: true, today: today);
            document.MarkScanClean("clean/key");

            db.SupplierDocuments.Add(document);
            codes.Add(document.ReferenceCode);
        }

        await db.SaveChangesAsync();

        return (supplier.ReferenceCode, codes[0], codes[1]);
    }
}
