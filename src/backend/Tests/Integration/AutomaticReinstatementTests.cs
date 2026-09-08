using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Suppliers;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// D-67: approving the replacement lifts the suspension BRULE-023 imposed.
///
/// <para><b>The argument, because it was contested and the correction stands.</b> The question was put as
/// "does automatic reinstatement reverse a suspension nobody re-examined", and the answer is that somebody
/// did: the supplier uploaded a replacement and a reviewer approved it. That approval IS the human check,
/// and it is what triggers this. Requiring a second person to confirm afterwards adds no information and
/// creates the worse failure - a supplier who has fixed the problem sitting locked out of tenders until
/// somebody notices.</para>
///
/// <para>So the tests here are mostly about the boundaries: reinstate when the rule's condition is
/// objectively gone, and refuse to when it is not, or when the suspension was a person's.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AutomaticReinstatementTests(PostgresApiFixture fixture)
{
    private const string CommercialRegistration = "commercial_registration";
    private const string TaxCertificate = "tax_certificate";

    /// <summary>An Active supplier holding one approved, expiry-tracked document of each named type, each
    /// already past its expiry date.</summary>
    private async Task<Guid> SeedSupplierWithExpiredDocumentsAsync(params string[] typeCodes)
    {
        var name = $"Reinstate {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).FirstAsync();
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
            foreach (var code in typeCodes)
            {
                var typeId = await db.DocumentTypes.Where(t => t.Code == code).Select(t => t.Id).SingleAsync();

                var document = SupplierDocument.CreatePendingScan(
                    $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, 1, "quarantine/key",
                    $"{code}-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
                    issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);
                document.MarkScanClean("clean/key");
                document.Approve(Guid.CreateVersion7());
                db.SupplierDocuments.Add(document);
                await db.SaveChangesAsync();

                // BRULE-020 refuses a past expiry at upload, so the date the clock would have produced is
                // written directly - the same technique AwardCriticalSuspensionTests documents.
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");
            }
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DocumentExpiryJob>().RunAsync(CancellationToken.None);
        }

        return supplierId;
    }

    /// <summary>Uploads a replacement of the named type as a new version, ready for a reviewer to approve.</summary>
    private async Task<string> UploadReplacementAsync(Guid supplierId, string typeCode)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var typeId = await db.DocumentTypes.Where(t => t.Code == typeCode).Select(t => t.Id).SingleAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        var previous = await db.SupplierDocuments
            .Where(d => d.SupplierId == supplierId && d.DocumentTypeId == typeId && d.IsLatestVersion)
            .ToListAsync();
        foreach (var old in previous) old.SupersedeWithNewVersion();

        var replacement = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}", supplierId, typeId, previous.Count + 1,
            "quarantine/key", $"renewed-{Guid.NewGuid():N}.pdf", "application/pdf", 2048, Guid.CreateVersion7(),
            issueDate: null, expiryDate: today.AddYears(1), expiryTracked: true, today: today);
        replacement.MarkScanClean("clean/key");

        db.SupplierDocuments.Add(replacement);
        await db.SaveChangesAsync();

        return replacement.ReferenceCode;
    }

    private async Task<SupplierLifecycleState> LifecycleOfAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId)
            .Select(s => s.LifecycleState).FirstAsync();
    }

    /// <summary>
    /// Approves one document as a reviewer would: read the supplier through the reviewer's own route, then
    /// send that read's ETag as the precondition.
    ///
    /// <para>The harness attaches an If-Match by probing PREFIXES of the write path, and a document decision
    /// has none it can read - the reviewer's view lives at /review/{code}, sideways from
    /// /suppliers/{code}/documents/... . That is the workaround the SPA also makes (F-4), and sending the
    /// header explicitly here is the test doing what the screen does rather than routing around the guard.</para>
    /// </summary>
    private async Task<HttpResponseMessage> ApproveAsync(HttpClient reviewer, string supplierCode, string documentCode)
    {
        var view = await reviewer.GetAsync($"/api/v1/review/{supplierCode}");
        view.IsSuccessStatusCode.Should().BeTrue(await view.Content.ReadAsStringAsync());
        view.Headers.ETag.Should().NotBeNull("the reviewer's read is what issues this decision's precondition");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/suppliers/{supplierCode}/documents/{documentCode}/approve");
        request.Headers.TryAddWithoutValidation("If-Match", view.Headers.ETag!.ToString());

        return await reviewer.SendAsync(request);
    }

    [Fact]
    public async Task Approving_the_replacement_reinstates_the_supplier_and_says_why()
    {
        var supplierId = await SeedSupplierWithExpiredDocumentsAsync(CommercialRegistration);
        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Suspended,
            "the control: BRULE-023 suspended them, and this test is about what undoes that");

        var replacementCode = await UploadReplacementAsync(supplierId, CommercialRegistration);

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var approved = await ApproveAsync(reviewer, await SupplierCodeAsync(supplierId), replacementCode);
        approved.IsSuccessStatusCode.Should().BeTrue(await approved.Content.ReadAsStringAsync());

        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Active,
            "the rule's condition is objectively gone, and the human check already happened - a reviewer "
            + "approved the replacement, which is what triggered this");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateId == supplierId && a.Action == "supplier_auto_reinstated")
            .SingleAsync();

        audit.Reason.Should().Contain(replacementCode,
            "the row names the document that fixed it - 'reactivated automatically' alone leaves the next "
            + "reader unable to tell why participation came back");
        audit.FromState.Should().Be(nameof(SupplierLifecycleState.Suspended));
        audit.ToState.Should().Be(nameof(SupplierLifecycleState.Active));
        audit.ActorUserId.Should().NotBeNull("the approving reviewer is the actor, not 'system'");

        // Filtered in memory: PayloadJson is a jsonb column, and a Contains() over it translates to LIKE,
        // which Postgres refuses on that type (42883). The rows are few and this is a test.
        var payloads = await db.OutboxMessages.AsNoTracking().Select(m => m.PayloadJson).ToListAsync();
        var told = payloads.Count(p => p.Contains(NotificationTypes.SupplierReinstated, StringComparison.Ordinal));
        told.Should().BeGreaterThan(0,
            "a system that says 'you are suspended' and stays silent when it lifts leaves a supplier "
            + "assuming the worst and not bidding");
    }

    [Fact]
    public async Task Fixing_one_of_two_expiries_does_not_reinstate()
    {
        // The predicate is about the SUPPLIER, not the document just approved. A supplier suspended by two
        // expired award-critical documents is not fixed by replacing one of them, and a per-document check
        // would have said otherwise.
        var supplierId = await SeedSupplierWithExpiredDocumentsAsync(CommercialRegistration, TaxCertificate);
        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Suspended);

        var replacementCode = await UploadReplacementAsync(supplierId, CommercialRegistration);
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var supplierCode = await SupplierCodeAsync(supplierId);
        (await ApproveAsync(reviewer, supplierCode, replacementCode)).IsSuccessStatusCode.Should().BeTrue();

        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Suspended,
            "one of the two award-critical documents is still expired, so the rule's condition has not gone");

        // And when the second is replaced too, they come back - otherwise this test would pass against a
        // reinstatement that never happens at all.
        var second = await UploadReplacementAsync(supplierId, TaxCertificate);
        (await ApproveAsync(reviewer, supplierCode, second)).IsSuccessStatusCode.Should().BeTrue();

        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Active);
    }

    [Fact]
    public async Task A_suspension_a_person_made_is_not_undone_by_a_document()
    {
        // The boundary that matters most. Somebody suspended this supplier for a reason of their own; a
        // document decision must not overturn a human one, and the audit trail is how the two are told apart.
        var name = $"Manual {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, name);

        Guid supplierId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            supplierId = await db.Suppliers.Where(s => s.DisplayNameEn == name).Select(s => s.Id).FirstAsync();
            await db.Suppliers.Where(s => s.Id == supplierId).ExecuteUpdateAsync(p => p
                .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
                .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));
        }

        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        var supplierCode = await SupplierCodeAsync(supplierId);

        var suspended = await reviewer.PostAsJsonAsync($"/api/v1/review/{supplierCode}/suspend",
            new { reason = "Under investigation by the buying body." });
        suspended.IsSuccessStatusCode.Should().BeTrue(await suspended.Content.ReadAsStringAsync());

        var replacementCode = await UploadReplacementAsync(supplierId, CommercialRegistration);
        (await ApproveAsync(reviewer, supplierCode, replacementCode)).IsSuccessStatusCode.Should().BeTrue();

        (await LifecycleOfAsync(supplierId)).Should().Be(SupplierLifecycleState.Suspended,
            "an automatic reinstatement may only undo an automatic suspension - reinstating here would be a "
            + "document decision overturning a person's");
    }

    private async Task<string> SupplierCodeAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Suppliers.AsNoTracking().Where(s => s.Id == supplierId)
            .Select(s => s.ReferenceCode).FirstAsync();
    }
}
