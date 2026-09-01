using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-84: the review queue is a table applications are inserted into (and removed from, via
/// approve/reject) continuously, so offset paging is the wrong tool - the audit-cursor regression
/// (MSP-66) lost 22 of 47 rows this exact way while page-one-only tests kept passing. These tests
/// walk every page and assert the UNION of rows, not just that page one looks right, and one of
/// them removes a not-yet-fetched supplier from the queue BETWEEN two page fetches - the scenario
/// that breaks offset paging (item shifts into the gap and gets skipped) but must not break
/// keyset paging (removing any row does not move the keyset position of the rows that remain).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReviewQueuePaginationTests(PostgresApiFixture fixture)
{
    private static Supplier MakeReadySupplier(string tag)
    {
        var s = Supplier.Register(
            referenceCode: $"SUP-{tag}-{Guid.NewGuid():N}"[..20],
            displayNameAr: "شركة اختبار",
            displayNameEn: $"Queue Test {tag} {Guid.NewGuid():N}"[..40],
            registrationNumber: null,
            primaryRepresentativeName: "Tester",
            primaryRepresentativeEmail: $"{tag}-{Guid.NewGuid():N}@example.com",
            primaryRepresentativePhone: "+963900000000");
        s.MarkEmailVerified();
        s.UpdateCoreProfile(null, null, null, "USD");
        s.AddAddress(AddressKind.HeadOffice, "L1", null, "Damascus", "DM", "SY", null, null, null);
        s.LinkCategory("CAT-1", isComplianceCritical: false);
        s.AcceptTerms("v1");
        return s;
    }

    private static Supplier MakeSubmitted(string tag)
    {
        var s = MakeReadySupplier(tag);
        s.Submit([]);
        return s;
    }

    private static Supplier MakeUnderReview(string tag)
    {
        var s = MakeSubmitted(tag);
        s.PickUpForReview();
        return s;
    }

    private static Supplier MakeInfoRequested(string tag)
    {
        var s = MakeUnderReview(tag);
        s.RequestInfo();
        return s;
    }

    private static Supplier MakeApproved(string tag)
    {
        var s = MakeUnderReview(tag);
        s.Approve([]);
        return s;
    }

    /// <summary>Walks the handler exactly as a real client would: follow NextCursor until HasMore
    /// is false. Returns every item seen, in the order returned.</summary>
    private async Task<List<ReviewQueueItemDto>> WalkAllAsync(IListReviewQueueHandler handler, int pageSize)
    {
        var all = new List<ReviewQueueItemDto>();
        string? cursor = null;
        do
        {
            var page = await handler.HandleAsync(cursor, pageSize, CancellationToken.None);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
            if (!page.HasMore) break;
        } while (cursor is not null);
        return all;
    }

    [Fact]
    public async Task Walking_all_pages_returns_every_queued_supplier_exactly_once_and_excludes_others()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IListReviewQueueHandler>();

        var submitted = MakeSubmitted("A");
        var underReview = MakeUnderReview("B");
        var infoRequested = MakeInfoRequested("C");
        var submitted2 = MakeSubmitted("D");
        var underReview2 = MakeUnderReview("E");
        var draft = Supplier.Register($"SUP-DRAFT-{Guid.NewGuid():N}"[..20], "شركة", "Draft Co",
            null, "Tester", $"draft-{Guid.NewGuid():N}@example.com");
        var approved = MakeApproved("F");

        var inQueue = new[] { submitted, underReview, infoRequested, submitted2, underReview2 };
        var notInQueue = new[] { draft, approved };

        db.Suppliers.AddRange([.. inQueue, .. notInQueue]);
        await db.SaveChangesAsync();

        // Page size 2 against 5 in-queue rows forces a 3-page walk (2, 2, 1) - the point is to
        // actually exercise cursor continuation, not return everything in one call.
        var walked = await WalkAllAsync(handler, pageSize: 2);

        // Denominator: assert against known reference codes rather than the table's total count,
        // since other tests in this collection may leave their own suppliers behind - the
        // property under test is "every row I created appears exactly once and nothing I
        // excluded appears", not "the table contains only my rows".
        var walkedCodes = walked.Select(w => w.ReferenceCode).ToList();
        walkedCodes.Should().OnlyHaveUniqueItems("keyset paging must never return the same row twice across a walk");

        foreach (var s in inQueue)
        {
            walkedCodes.Should().Contain(s.ReferenceCode, $"{s.ReferenceCode} is in a review-queue state and must be reachable by walking every page");
        }
        foreach (var s in notInQueue)
        {
            walkedCodes.Should().NotContain(s.ReferenceCode, $"{s.ReferenceCode} is not in a review-queue state and must never appear");
        }
    }

    /// <summary>FEAT-03.6/FR-ONB-012: EnteredQueueAt must reflect the most recent time this
    /// application (re)entered the active queue - not the original submission, and not
    /// Supplier.CreatedAt (registration date). A stale "resubmitted 3 days ago" reading would be
    /// exactly the kind of misleading age indicator FEAT-03.6 exists to prevent.</summary>
    [Fact]
    public async Task EnteredQueueAt_reflects_the_most_recent_resubmission_not_the_original_submission_or_registration()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IListReviewQueueHandler>();

        var supplier = MakeUnderReview("RESUB");
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var originalSubmission = DateTimeOffset.UtcNow.AddDays(-10);
        var recentResubmission = DateTimeOffset.UtcNow.AddHours(-3);

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = originalSubmission,
            ActorKind = AuditActorKind.User,
            AggregateType = "Supplier",
            AggregateId = supplier.Id,
            Action = "application_submitted",
            CorrelationId = Guid.CreateVersion7(),
        });
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = recentResubmission,
            ActorKind = AuditActorKind.User,
            AggregateType = "Supplier",
            AggregateId = supplier.Id,
            Action = "application_resubmitted",
            CorrelationId = Guid.CreateVersion7(),
        });
        await db.SaveChangesAsync();

        var page = await handler.HandleAsync(null, 50, CancellationToken.None);
        var item = page.Items.Should().ContainSingle(i => i.ReferenceCode == supplier.ReferenceCode).Subject;

        item.EnteredQueueAt.Should().BeCloseTo(recentResubmission, TimeSpan.FromSeconds(1),
            "the newer resubmission must win over both the original submission and Supplier.CreatedAt");
        item.EnteredQueueAt.Should().NotBeCloseTo(originalSubmission, TimeSpan.FromDays(1),
            "using the stale original-submission timestamp would make a just-resubmitted application read as 10 days old");
    }

    [Fact]
    public async Task A_supplier_leaving_the_queue_between_page_fetches_does_not_skip_the_rows_that_remain()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IListReviewQueueHandler>();

        // Five suppliers, created in order so CreatedAt/Id ascending (the queue's sort) matches
        // creation order: s1..s5.
        var s1 = MakeSubmitted("H1");
        var s2 = MakeUnderReview("H2");
        var s3 = MakeSubmitted("H3");
        var s4 = MakeUnderReview("H4");
        var s5 = MakeSubmitted("H5");
        db.Suppliers.AddRange(s1, s2, s3, s4, s5);
        await db.SaveChangesAsync();

        // Page 1, size 2: expect s1, s2 (oldest-first order), with a cursor for continuation.
        var page1 = await handler.HandleAsync(null, 2, CancellationToken.None);
        page1.Items.Select(i => i.ReferenceCode).Should().BeEquivalentTo([s1.ReferenceCode, s2.ReferenceCode]);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        // Between page fetches: s1 - already returned in page 1 - leaves the queue (approved).
        // This is the shape that breaks OFFSET paging specifically: removing a row before the
        // fetch boundary shifts every later row's position back by one, so a naive
        // Skip(2).Take(2) on the next call would now land on [s4, s5] and silently drop s3 -
        // never returned on either page. The keyset cursor is immune because it was never
        // counting positions, only comparing against s2's own (CreatedAt, Id) value.
        using (var mutScope = fixture.Services.CreateScope())
        {
            var mutDb = mutScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var toRemove = await mutDb.Suppliers.FirstAsync(s => s.Id == s1.Id);
            toRemove.PickUpForReview();
            toRemove.Approve([]);
            await mutDb.SaveChangesAsync();
        }

        var page2 = await handler.HandleAsync(page1.NextCursor, 2, CancellationToken.None);

        // s3 must be present - this is the row a position-based page 2 would have dropped.
        page2.Items.Select(i => i.ReferenceCode).Should().BeEquivalentTo([s3.ReferenceCode, s4.ReferenceCode],
            "s1 leaving the queue must not shift s3 out of view - it was never counted by position");
    }
}
