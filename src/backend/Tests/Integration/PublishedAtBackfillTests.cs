using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §12-A/C: <c>rfq.rfq.PublishedAt</c>, the column §12.4's <c>publishedAt</c> and §6.3's
/// <c>-publishedAt</c> both needed and that did not exist.
///
/// <para><b>The distinction that makes this a new column rather than a rename.</b> PublishAt is a
/// nullable, freely-editable SCHEDULED time supplied at creation - an intent. An RFQ published
/// immediately has PublishAt null. PublishedAt records that publication actually happened.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PublishedAtBackfillTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Publishing_sets_published_at_even_when_no_publish_was_scheduled()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"PubAt {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        // publishAt deliberately null: the scheduled time is absent, so a test that confused the two
        // columns would see null here and pass for the wrong reason.
        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "PublishedAt RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{code}/evaluation-template", new { evaluationTemplateId = templateId });

        // Publishing requires at least one invitee (BUSINESS-PROCESSES.md §3.1: publish "generates
        // access"), so a real supplier is seeded rather than the publish call being fudged.
        var supplierName = $"PubAt {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, supplierName);
        Guid supplierId;
        await using (var seedScope = fixture.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await seedDb.Suppliers.FirstAsync(x => x.DisplayNameEn == supplierName);
            supplierId = supplier.Id;
            await seedDb.Suppliers.Where(x => x.Id == supplierId).ExecuteUpdateAsync(pp => pp
                .SetProperty(x => x.OnboardingState, MotsSupplierPortal.Domain.Suppliers.SupplierOnboardingState.Approved)
                .SetProperty(x => x.LifecycleState, MotsSupplierPortal.Domain.Suppliers.SupplierLifecycleState.Active));
        }
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations", new { supplierId });

        await officer.PostAsync($"/api/v1/rfqs/{code}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{code}/approve", null);

        var before = DateTimeOffset.UtcNow;
        (await officer.PostAsync($"/api/v1/rfqs/{code}/publish", null)).EnsureSuccessStatusCode();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfq = await db.Rfqs.AsNoTracking().FirstAsync(r => r.ReferenceCode == code);

        rfq.PublishAt.Should().BeNull("nothing was scheduled - this is the field that used to be mistaken for the other");
        rfq.PublishedAt.Should().NotBeNull();
        rfq.PublishedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task An_unpublished_rfq_has_no_published_at()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "مسودة", titleEn = "Draft RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = DateTimeOffset.UtcNow.AddDays(3),
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(4), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(9),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfq = await db.Rfqs.AsNoTracking().FirstAsync(r => r.ReferenceCode == code);

        rfq.PublishAt.Should().NotBeNull("a publish WAS scheduled");
        rfq.PublishedAt.Should().BeNull("but it has not happened - which is exactly why the two columns are different");
    }

    /// <summary>
    /// The backfill's correctness condition: no RFQ may be published-or-later while carrying no
    /// PublishedAt.
    ///
    /// <para><b>Scoped to this test's own rows, deliberately.</b> The first version swept EVERY row
    /// in the database and failed in the full suite while passing alone - because the integration
    /// suite shares one database, and a sibling test in this very class reconstructs the
    /// pre-migration state (Published, PublishedAt NULL) on purpose before running the backfill.
    /// A global assertion over concurrently-mutated shared state is not a stable check; it is a
    /// race that reports whichever moment it happened to observe. That is the same class of
    /// instrument-over-a-moving-denominator this project has been bitten by before, so it is fixed
    /// rather than retried.</para>
    ///
    /// <para>The global sweep still has a place - at deploy time, immediately after the migration,
    /// where nothing is concurrently publishing. It is recorded in the batch report as a query to
    /// run there, not as a test here.</para>
    /// </summary>
    [Fact]
    public async Task A_published_rfq_created_by_this_test_always_carries_a_published_at()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var code = await PublishOneAsync(officer, manager);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfq = await db.Rfqs.AsNoTracking().FirstAsync(r => r.ReferenceCode == code);

        rfq.State.Should().NotBe(RfqState.Draft);
        rfq.PublishedAt.Should().NotBeNull(
            "a published RFQ without a PublishedAt would be one whose publication left no record");

        // And the audit row the backfill reads from exists for it, which is what makes the
        // migration's recovery possible for rows that predate the column.
        var auditRows = await db.AuditLogs.CountAsync(a =>
            a.AggregateType == "Rfq" && a.AggregateId == rfq.Id && a.Action == "rfq_published");
        auditRows.Should().Be(1);
    }

    /// <summary>
    /// The BACKFILL STATEMENT itself, not merely "the migration ran".
    ///
    /// <para>On a fresh database every RFQ is published after the column exists, so the migration's
    /// UPDATE matches nothing and passing proves only that it did not crash. This reconstructs the
    /// pre-migration state deliberately - an RFQ in Published with PublishedAt NULL, and a real
    /// <c>rfq_published</c> audit row - then runs the migration's own SQL, verbatim, and asserts the
    /// value recovered is the audit row's instant rather than CreatedAt.</para>
    /// </summary>
    [Fact]
    public async Task The_backfill_recovers_the_publication_instant_from_the_audit_trail()
    {
        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "أثر", titleEn = "Backfill RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var publishedAt = new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfq = await db.Rfqs.FirstAsync(r => r.ReferenceCode == code);

        // Reconstruct the pre-migration world: Published, no PublishedAt, one audit row.
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE rfq.rfq SET "State" = 'Published', "PublishedAt" = NULL WHERE "Id" = {0}""".Replace("{0}", $"'{rfq.Id}'"));
        db.AuditLogs.Add(new MotsSupplierPortal.Domain.Audit.AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = publishedAt,
            ActorKind = MotsSupplierPortal.Domain.Audit.AuditActorKind.System,
            AggregateType = "Rfq",
            AggregateId = rfq.Id,
            Action = "rfq_published",
            CorrelationId = Guid.CreateVersion7(),
        });
        await db.SaveChangesAsync();

        // The migration's statement, verbatim.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE rfq.rfq AS r
            SET "PublishedAt" = a.first_published
            FROM (
                SELECT "AggregateId" AS rfq_id, MIN("OccurredAt") AS first_published
                FROM ops.audit_log
                WHERE "AggregateType" = 'Rfq' AND "Action" = 'rfq_published'
                GROUP BY "AggregateId"
            ) AS a
            WHERE r."Id" = a.rfq_id AND r."PublishedAt" IS NULL;
            """);

        var backfilled = await db.Rfqs.AsNoTracking().FirstAsync(r => r.Id == rfq.Id);
        backfilled.PublishedAt.Should().Be(publishedAt,
            "the audit row's OccurredAt is the real publication instant - CreatedAt would be wrong " +
            "by however long the RFQ sat in Draft and InternalReview");
    }

    /// <summary>Drives one RFQ all the way to Published and returns its reference code.</summary>
    private async Task<string> PublishOneAsync(HttpClient officer, HttpClient manager)
    {
        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"PubOne {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Publish One", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        await officer.PutAsJsonAsync($"/api/v1/rfqs/{code}/evaluation-template", new { evaluationTemplateId = templateId });

        var supplierName = $"PubOne {Guid.NewGuid():N}"[..24];
        await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, supplierName);
        await using (var seedScope = fixture.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await seedDb.Suppliers.FirstAsync(x => x.DisplayNameEn == supplierName);
            await seedDb.Suppliers.Where(x => x.Id == supplier.Id).ExecuteUpdateAsync(pp => pp
                .SetProperty(x => x.OnboardingState, MotsSupplierPortal.Domain.Suppliers.SupplierOnboardingState.Approved)
                .SetProperty(x => x.LifecycleState, MotsSupplierPortal.Domain.Suppliers.SupplierLifecycleState.Active));
            await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations", new { supplierId = supplier.Id });
        }

        await officer.PostAsync($"/api/v1/rfqs/{code}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{code}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{code}/publish", null)).EnsureSuccessStatusCode();
        return code;
    }
}
