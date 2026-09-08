using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// D-58's consequence, end to end: an expired commercial register stops the supplier bidding.
///
/// <para><b>Why this is the condition D-58 shipped with.</b> BRULE-023's auto-suspend has existed since
/// the column was added and had never once run against a real value - every seeded document type
/// carried <c>IsAwardCritical = false</c>, because which documents are award-critical was nobody's
/// decision to guess. <see cref="AwardCriticalSuspensionTests"/> covers the job's own behaviour by
/// setting the flag temporarily and reading the lifecycle column back. What nothing covered was what
/// the suspension is FOR.</para>
///
/// <para>The chain is four links long and each was written by a different batch: the expiry job finds
/// the expired document, the job suspends the supplier because its type is award-critical, the
/// supplier's LifecycleState leaves Active, and <c>StartProposalHandler</c> refuses a supplier who is
/// not Active. A break anywhere in it leaves a supplier suspended on paper and bidding in practice -
/// an enforcement everyone believes in and nobody has seen.</para>
///
/// <para><b>Two tenders, not one.</b> Starting a proposal is idempotent: a second start on the same
/// RFQ returns the draft that already exists and never reaches the Active check. So the bid that must
/// be refused has to be a bid the supplier had not already started - which is also the real case, a
/// suspended supplier answering the next invitation.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AwardCriticalBlocksBiddingTests(PostgresApiFixture fixture)
{
    private const string CommercialRegistration = "commercial_registration";

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    /// <summary>One published RFQ in SubmissionOpen with this supplier invited, driven through the
    /// real approve/publish path and the real timeline job.</summary>
    private async Task<string> OpenRfqInvitingAsync(Guid supplierId, string titleEn)
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        // An active evaluation template, because the review gate refuses a tender without one - the
        // publish then fails as "Cannot publish from state 'Draft'", three steps from the cause.
        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Template {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "معيار", nameEn = "Only Criterion", dimension = "Technical", weight = 100, maxScore = 10,
            threshold = (int?)null, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب اختبار", titleEn, descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddSeconds(1),
            submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var referenceCode = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{referenceCode}/evaluation-template",
            new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{referenceCode}/invitations", new { supplierId });

        var submitted = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/submit-review", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        var approved = await manager.PostAsync($"/api/v1/rfqs/{referenceCode}/approve", null);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var publish = await officer.PostAsync($"/api/v1/rfqs/{referenceCode}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RfqTimelineJob>().RunAsync(CancellationToken.None);
        }

        return referenceCode;
    }

    /// <summary>An approved commercial register for this supplier whose expiry date has passed. The
    /// date is written in storage rather than sent to the API because BRULE-020 refuses a past expiry
    /// at upload - correctly; the state under test is the one the passage of time produces, not one an
    /// officer could type.</summary>
    private async Task ExpireCommercialRegisterAsync(Guid supplierId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var typeId = await db.DocumentTypes.Where(t => t.Code == CommercialRegistration)
            .Select(t => t.Id).SingleAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        var document = SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            supplierId, typeId, 1, "quarantine/key", $"register-{Guid.NewGuid():N}.pdf",
            "application/pdf", 2048, Guid.CreateVersion7(),
            issueDate: null, expiryDate: today.AddDays(1), expiryTracked: true, today: today);

        document.MarkScanClean("clean/key");
        document.Approve(Guid.CreateVersion7());

        db.SupplierDocuments.Add(document);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlAsync(
            $"UPDATE supplier.supplier_document SET \"ExpiryDate\" = {today.AddDays(-1)} WHERE \"Id\" = {document.Id}");
    }

    private async Task RunExpiryJobAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<Infrastructure.Suppliers.DocumentExpiryJob>();
        await job.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_expired_commercial_register_suspends_the_supplier_and_stops_them_bidding()
    {
        var (supplier, supplierId) = await ActiveSupplierAsync($"Register {Guid.NewGuid():N}"[..24]);

        var firstRfq = await OpenRfqInvitingAsync(supplierId, "Bid before expiry");
        var secondRfq = await OpenRfqInvitingAsync(supplierId, "Bid after expiry");

        // The control, asserted first so a refusal below is the expiry doing it rather than the
        // fixture never having worked.
        var beforeExpiry = await supplier.PostAsync($"/api/v1/rfqs/{firstRfq}/proposals", null);
        beforeExpiry.StatusCode.Should().Be(HttpStatusCode.OK, await beforeExpiry.Content.ReadAsStringAsync());

        await ExpireCommercialRegisterAsync(supplierId);
        await RunExpiryJobAsync();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.Suppliers.Where(s => s.Id == supplierId)
                .Select(s => s.LifecycleState).SingleAsync();

            state.Should().Be(SupplierLifecycleState.Suspended,
                "D-58 made the commercial register award-critical, and BRULE-023 suspends a supplier " +
                "whose award-critical document has expired");
        }

        var afterExpiry = await supplier.PostAsync($"/api/v1/rfqs/{secondRfq}/proposals", null);

        afterExpiry.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a suspended supplier is refused the tender the same way an uninvited one is - the " +
            "refusal is what the suspension is for");
    }

    /// <summary>
    /// The shipped values, named. This is the assertion that fails if a later change flips a third
    /// type on, or clears one of these two - both of which are decisions with the same consequence
    /// D-58 weighed, and neither of which should reach a deployment as a diff nobody read.
    /// </summary>
    [Fact]
    public async Task The_two_types_D58_names_are_the_ones_shipped_as_award_critical()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var flags = await db.DocumentTypes.AsNoTracking()
            .Select(t => new { t.Code, t.IsAwardCritical }).ToListAsync();

        flags.Should().NotBeEmpty("the reference data must be seeded for this assertion to mean anything");
        flags.Where(t => t.IsAwardCritical).Select(t => t.Code)
            .Should().BeEquivalentTo([CommercialRegistration, "tax_certificate"],
                "D-58: legal capacity to hold a contract, not standing");
        flags.Should().Contain(t => t.Code == "chamber_membership" && !t.IsAwardCritical,
            "chamber membership evidences standing rather than capacity, so its expiry is a flag, not a bar");
    }
}
