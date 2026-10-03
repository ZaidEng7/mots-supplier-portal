// The needs attention section of the administrator's dashboard, read through the real route.
//
// Which item each figure raises, where each one links and how hidden and failed sections are treated are tested
// on hand-built results in DashboardNeedsAttentionTests, a unit test, where every combination is cheap. This file
// proves the parts that need the database and the real host: the review deadline, which this section counts with
// a query of its own, an item computed end to end from a real section's figure, and the review deadline's own
// failure, which must leave the rest of the section answering.
//
// The database is shared by the whole suite and never reset, so no count here is asserted as a total. Each test
// reads the section, seeds rows of its own, reads it again and asserts how far the count moved. Seeded suppliers
// and accounts are deleted at the end. Their audit rows stay, because the audit table is append-only, and they
// name suppliers that no longer exist, which no count reads.
//
//
// THE REVIEW DEADLINE IS TESTED AT ITS EDGES
//
// The target is a number of working days, read from the review.slaWorkingDays setting, with Friday and Saturday
// as the weekend. Two suppliers sit on either side of it: one that entered the queue just long enough ago for its
// target to have passed a few minutes before the read, and one that entered just recently enough for its target
// to fall a few minutes after it. They are found by walking back from now with ReviewSla itself, so the test holds
// on any day of the week. A count in calendar days, or one working day too many or too few, moves one of them to
// the wrong side.
//
// Around them sit the rows that must not be counted: an application past its target but waiting on the supplier
// (InfoRequested, where the timer pauses), one already approved, one whose older entry is past the target but
// which was resubmitted since, and one that entered three calendar days ago, past the queue's own 48-hour tone
// and inside the target.
//
//
// A VIEWER WITHOUT THE REVIEW QUEUE GETS THE ITEM WITHOUT ITS LINK
//
// That viewer is a ministry viewer granted admin.users.manage for one test, the way AdminDashboardFrameTests does
// it: the role's whole set is read first and written back in a finally.

namespace MotsSupplierPortal.Tests.Integration.Admin.Dashboard;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Admin.Dashboard;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;
using Xunit;

[Collection(IntegrationTestCollection.Name)]
public sealed class NeedsAttentionSectionTests(PostgresApiFixture fixture)
{
    private const string Route = "/api/v1/admin/dashboard";

    [Fact]
    public async Task The_review_deadline_counts_submitted_and_under_review_cases_past_their_working_day_target()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var days = await SlaWorkingDaysAsync();
        var now = DateTimeOffset.UtcNow;

        var justPast = EnteredSoTheTargetIsBefore(now.AddMinutes(-5), days);
        var justInside = EnteredSoTheTargetIsAfter(now.AddMinutes(10), days);
        var longAgo = now.AddDays(-30);
        var threeCalendarDaysAgo = now.AddDays(-3);

        ReviewSla.TargetFor(justPast, days).Should().BeBefore(now, "the edge case must really be past its target");
        ReviewSla.TargetFor(justInside, days).Should().BeAfter(now.AddMinutes(5), "and its neighbour really inside");
        ReviewSla.TargetFor(threeCalendarDaysAgo, days).Should().BeAfter(now, "three calendar days is inside any working-day target");

        var before = ReviewDeadline(await NeedsAttentionAsync(admin));

        var seeded = new List<Guid>();
        try
        {
            await SeedAsync(seeded, Submitted("PAST"), (longAgo, "application_submitted"));
            await SeedAsync(seeded, UnderReview("EDGE"), (justPast, "application_review_resumed"));

            await SeedAsync(seeded, UnderReview("INSIDE"), (justInside, "application_submitted"));
            await SeedAsync(seeded, InfoRequested("PAUSED"), (longAgo, "application_submitted"));
            await SeedAsync(seeded, Approved("DONE"), (longAgo, "application_submitted"));
            await SeedAsync(seeded, Submitted("RESUB"),
                (longAgo, "application_submitted"), (now.AddHours(-2), "application_resubmitted"));
            await SeedAsync(seeded, Submitted("TONE"), (threeCalendarDaysAgo, "application_submitted"));
            await SeedAsync(seeded, Submitted("FRESH"));

            var section = await NeedsAttentionAsync(admin);
            var after = ReviewDeadline(section);

            (after.Count - before.Count).Should().Be(2,
                "the Submitted and UnderReview cases past their target are counted; the paused, decided, resubmitted, "
                + "inside-the-target and fresh ones are not");
            after.Link.Should().Be("/back-office/review", "the administrator holds supplier.review");
            section.GetProperty("allClear").GetBoolean().Should().BeFalse();
            section.GetProperty("checksNotRun").EnumerateArray().Select(k => k.GetString())
                .Should().NotContain(DashboardNeedsAttentionChecks.ReviewDeadlinePassed);

            await AsViewerWithoutTheReviewQueueAsync(admin, async viewer =>
            {
                var limited = ReviewDeadline(await NeedsAttentionAsync(viewer));
                limited.Count.Should().Be(after.Count, "the count is the same for every viewer of the dashboard");
                limited.Link.Should().BeNull("a viewer without supplier.review would be refused at the review queue");
            });
        }
        finally
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => seeded.Contains(s.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_locked_out_staff_account_raises_an_item_that_links_to_the_staff_list()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var before = Item(await NeedsAttentionAsync(admin), DashboardNeedsAttentionChecks.StaffLockedOut);

        var userId = await LockedOutStaffAsync();

        try
        {
            var after = Item(await NeedsAttentionAsync(admin), DashboardNeedsAttentionChecks.StaffLockedOut);

            (after.Count - before.Count).Should().Be(1);
            after.Link.Should().Be("/back-office/staff");
        }
        finally
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_review_deadline_that_cannot_be_counted_is_not_run_and_the_rest_still_answer()
    {
        var admin = await StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);
        var log = new LogRecorder();
        var fixtureKey = fixture.Services.GetRequiredService<JwtSigningKeyProvider>().GetValidationKey();

        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ISystemSettingReader, ReviewSlaUnreadable>();
            services.AddSingleton<ILogger<NeedsAttentionSectionHandler>>(log);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.TokenValidationParameters.IssuerSigningKey = fixtureKey);
        }));

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var lockedOut = await LockedOutStaffAsync();
        try
        {
            var response = await client.GetAsync(Route);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            text.Should().NotContain(ReviewSlaUnreadable.Secret, "the failure stays on the server");

            var body = JsonDocument.Parse(text).RootElement.GetProperty("needsAttention");
            body.GetProperty("status").GetString().Should().Be("ok", "one check failing does not fail the section");

            var section = body.GetProperty("data");
            section.GetProperty("checksNotRun").EnumerateArray().Select(k => k.GetString())
                .Should().Contain(DashboardNeedsAttentionChecks.ReviewDeadlinePassed);
            section.GetProperty("allClear").GetBoolean().Should().BeFalse();
            Item(section, DashboardNeedsAttentionChecks.StaffLockedOut).Count.Should().BeGreaterThan(0,
                "the checks computed from the other sections still answer");

            log.Entries.Should().Contain(e =>
                e.Level == LogLevel.Error && e.Exception != null && e.Exception.Message == ReviewSlaUnreadable.Secret);
        }
        finally
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == lockedOut).ExecuteDeleteAsync();
        }
    }

    private async Task<Guid> LockedOutStaffAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var email = $"needs-attention-locked-{Guid.NewGuid():N}@ministry.example";
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = "Locked Out Person",
            EmailConfirmed = true,
            IsActive = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddHours(1),
        };
        var created = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(user);
        created.Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<int> SlaWorkingDaysAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISystemSettingReader>()
            .GetIntAsync(SystemSettings.ReviewSlaWorkingDays, CancellationToken.None);
    }

    // The newest entry time whose target falls before the given moment, stepping back ten minutes at a time.
    private static DateTimeOffset EnteredSoTheTargetIsBefore(DateTimeOffset moment, int days)
    {
        var entered = moment;
        while (ReviewSla.TargetFor(entered, days) >= moment)
        {
            entered = entered.AddMinutes(-10);
        }

        return entered;
    }

    // The oldest entry time whose target falls after the given moment, stepping forward ten minutes at a time.
    private static DateTimeOffset EnteredSoTheTargetIsAfter(DateTimeOffset moment, int days)
    {
        var entered = EnteredSoTheTargetIsBefore(moment, days);
        while (ReviewSla.TargetFor(entered, days) <= moment)
        {
            entered = entered.AddMinutes(10);
        }

        return entered;
    }

    private async Task SeedAsync(List<Guid> seeded, Supplier supplier, params (DateTimeOffset At, string Action)[] entries)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Suppliers.Add(supplier);
        seeded.Add(supplier.Id);

        foreach (var (at, action) in entries)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = at,
                ActorKind = AuditActorKind.User,
                AggregateType = "Supplier",
                AggregateId = supplier.Id,
                Action = action,
                CorrelationId = Guid.CreateVersion7(),
            });
        }

        await db.SaveChangesAsync();
    }

    private static Supplier Ready(string tag)
    {
        var s = Supplier.Register(
            referenceCode: $"SUP-NA{tag}-{Guid.NewGuid():N}"[..20],
            displayNameAr: "شركة اختبار",
            displayNameEn: $"Needs Attention {tag} {Guid.NewGuid():N}"[..40],
            registrationNumber: null,
            primaryRepresentativeName: "Tester",
            primaryRepresentativeEmail: $"na-{tag}-{Guid.NewGuid():N}@example.com",
            primaryRepresentativePhone: "+963900000000");
        s.MarkEmailVerified();
        s.UpdateCoreProfile(null, null, null, "USD");
        s.AddAddress(AddressKind.HeadOffice, "L1", null, "Damascus", "DM", "SY", null, null, null);
        s.LinkCategory("CAT-1", isComplianceCritical: false);
        s.AcceptTerms("v1");
        return s;
    }

    private static Supplier Submitted(string tag)
    {
        var s = Ready(tag);
        s.Submit([]);
        return s;
    }

    private static Supplier UnderReview(string tag)
    {
        var s = Submitted(tag);
        s.PickUpForReview();
        return s;
    }

    private static Supplier InfoRequested(string tag)
    {
        var s = UnderReview(tag);
        s.RequestInfo();
        return s;
    }

    private static Supplier Approved(string tag)
    {
        var s = UnderReview(tag);
        s.Approve([]);
        return s;
    }

    private static async Task<JsonElement> NeedsAttentionAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var section = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("needsAttention");
        section.GetProperty("status").GetString().Should().Be("ok");
        return section.GetProperty("data");
    }

    private static (int Count, string? Link) ReviewDeadline(JsonElement section) =>
        Item(section, DashboardNeedsAttentionChecks.ReviewDeadlinePassed);

    // An item's count and link, or a count of zero and no link when the check did not fire.
    private static (int Count, string? Link) Item(JsonElement section, string key)
    {
        foreach (var item in section.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("key").GetString() == key)
            {
                return (item.GetProperty("count").GetInt32(), item.GetProperty("link").GetString());
            }
        }

        return (0, null);
    }

    private async Task AsViewerWithoutTheReviewQueueAsync(HttpClient admin, Func<HttpClient, Task> act)
    {
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        var original = roles.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == Roles.MinistryViewer)
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();

        try
        {
            await SetPermissionsAsync(admin,
                [.. original.Where(p => p != Permissions.SupplierReview), Permissions.AdminUsersManage]);
            await act(await StaffTestClient.CreateAsync(fixture, Roles.MinistryViewer));
        }
        finally
        {
            await SetPermissionsAsync(admin, original);
        }
    }

    private static async Task SetPermissionsAsync(HttpClient admin, string[] permissions)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/roles/{Roles.MinistryViewer}/permissions", new { permissions = permissions.Distinct().ToArray() });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // The real setting reader for every key but the review target, which throws, as a broken settings row would.
    private sealed class ReviewSlaUnreadable(AppDbContext db, IConfiguration configuration) : ISystemSettingReader
    {
        public const string Secret = "review-sla-unreadable-detail";

        private readonly SystemSettingReader _inner = new(db, configuration);

        public Task<string> GetAsync(string key, CancellationToken ct) =>
            key == SystemSettings.ReviewSlaWorkingDays
                ? throw new InvalidOperationException(Secret)
                : _inner.GetAsync(key, ct);
    }

    private sealed class LogRecorder : ILogger<NeedsAttentionSectionHandler>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
