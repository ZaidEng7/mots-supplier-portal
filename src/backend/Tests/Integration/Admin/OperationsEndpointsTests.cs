// The two operations screens, SCR-721 and SCR-722. T-083 left both of them as bare counters; these are
// the per-row views and the two actions an operator can take from them.
//
// The recurring-job list is driven by the list of jobs we EXPECT, not by what Hangfire happens to hold. A
// monitor that listed only what exists could never show the one thing worth showing, which is a job that
// should be there and is not; the test fixture registers no recurring jobs, so the first test asserts
// exactly that case. The fault is reported per row rather than as a count, so every row says whether
// Hangfire holds that job. "Run now" on a job a deployment dropped answers 404 rather than a cheerful
// 202: an operator pressing it has to learn that nothing happened, and an expected-but-unregistered job
// answers the same way, because the question the endpoint asks is "can Hangfire run this" and not "did we
// mean to register it".
//
// The outbox view reports every status including the empty ones - an operator who cannot see "Failed: 0"
// cannot tell it from a count that failed to load. The status filter is parsed strictly, and that is the
// failure this closes: the first version used Enum.TryParse and ignored failure, so a typo applied NO
// predicate and returned every row, meaning the caller asked to narrow and got the opposite with nothing
// to tell them. FilterGuardTests caught it, not review. Both controls are here: a recognised value is
// accepted, and the multi-value OR form from PROBLEM-STATEMENT.md §6.2 works, which is the question an
// operator actually asks - "show me everything not yet delivered". The award filter carries the same guard
// for the same reason.
//
// Replay is refused on anything but a failed message, and that half is the one that matters: the outbox
// exists to make delivery exactly-once, and an admin button that re-sends a delivered integration event
// breaks the guarantee the whole pattern is for. The successful replay is asserted on the row rather than
// on the response, because replay means Pending again with no processed time, so the dispatcher picks it
// up on its next pass.
//
// SCR-723 reports whether a real ERP transport exists at all. BRULE-011, and the reason this field is on
// the DTO rather than left to the reader: EPIC-23's adapter has not landed, so what is registered is a
// logging stand-in that accepts everything and sends nothing. A column of "Synced" without this flag
// would be an instrument asserting something untrue, so False is the CORRECT answer here and the assertion
// says so deliberately.
//
// SCR-726 is asserted against the values Program.cs configures Identity with, so if someone changes the
// policy and not this test the test fails - which is the point: a posture screen that can disagree with
// the enforcement it describes is worse than no screen, because it would be believed. The negative half is
// the one worth having on a screen like this: policy numbers only. A posture endpoint that grew a signing
// key or a connection string would be the most valuable request in the API to an attacker holding an admin
// session.
//
// SCR-725 is asserted against FileTypeSniffer's own constants, which are what UploadDocumentHandler checks
// and what DocumentEndpoints sizes its multipart limit from. If the cap moves and this screen does not,
// the test fails, which is the only way a reported limit stays the enforced one. Both halves of the type
// rule are reported because the PAIRING is the rule: §4.1 refuses a .pdf whose magic bytes are a PNG, and
// a list of bare extensions would hide that entirely.
//
// The last test is the control, so every refusal above is about the permission and not about a route that
// refuses everyone.

namespace MotsSupplierPortal.Tests.Integration.Admin;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class OperationsEndpointsTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    private async Task<Guid> SeedOutboxAsync(OutboxSyncStatus status)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = $"test.event.{Guid.NewGuid():N}"[..24],
            PayloadJson = """{"probe":true}""",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SyncStatus = status,
            ProcessedAt = status == OutboxSyncStatus.Pending ? null : DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    [Fact]
    public async Task Every_expected_job_gets_a_row_whether_or_not_Hangfire_holds_it()
    {
        var admin = await AdminAsync();

        var monitor = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/jobs");
        var ids = monitor.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("id").GetString()).ToList();

        ids.Should().Contain(RecurringJobs.All);

        var rows = monitor.GetProperty("jobs").EnumerateArray().ToList();
        rows.Should().NotBeEmpty();
        rows.Select(row => row.GetProperty("registered").GetBoolean()).Should().AllSatisfy(
            registered => registered.Should().BeFalse("this fixture registers no recurring jobs"));
    }

    [Fact]
    public async Task Triggering_a_job_Hangfire_does_not_hold_is_a_404_not_a_cheerful_202()
    {
        var admin = await AdminAsync();

        (await admin.PostAsync("/api/v1/admin/jobs/not-a-real-job/trigger", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/api/v1/admin/jobs/{RecurringJobs.All[0]}/trigger", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "this fixture registers no recurring jobs, so nothing here is runnable");
    }

    [Fact]
    public async Task The_outbox_reports_every_status_including_the_empty_ones()
    {
        await SeedOutboxAsync(OutboxSyncStatus.Failed);
        var admin = await AdminAsync();

        var monitor = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/outbox");
        var counts = monitor.GetProperty("counts");

        foreach (var status in Enum.GetNames<OutboxSyncStatus>())
        {
            counts.TryGetProperty(status, out _).Should().BeTrue($"{status} must be reported even at zero");
        }

        counts.GetProperty("Failed").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task An_unrecognised_status_is_refused_rather_than_returning_everything()
    {
        await SeedOutboxAsync(OutboxSyncStatus.Sent);
        await SeedOutboxAsync(OutboxSyncStatus.Pending);
        var admin = await AdminAsync();

        var refused = await admin.GetAsync("/api/v1/admin/outbox?status=Faild");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().Should().EndWith("/errors/validation");

        (await admin.GetAsync("/api/v1/admin/outbox?status=Sent")).StatusCode.Should().Be(HttpStatusCode.OK);

        var both = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/outbox?status=Failed,Pending");
        var states = both.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("syncStatus").GetString())
            .ToList();

        states.Should().NotBeEmpty("the Pending row seeded above must come back, or this asserts over nothing");
        states.Should().OnlyContain(state => state == "Failed" || state == "Pending",
            "a Sent message must not come back from a Failed,Pending filter");
    }

    [Fact]
    public async Task Only_a_failed_message_replays()
    {
        var failed = await SeedOutboxAsync(OutboxSyncStatus.Failed);
        var sent = await SeedOutboxAsync(OutboxSyncStatus.Sent);
        var pending = await SeedOutboxAsync(OutboxSyncStatus.Pending);
        var admin = await AdminAsync();

        (await admin.PostAsync($"/api/v1/admin/outbox/{failed}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await admin.PostAsync($"/api/v1/admin/outbox/{sent}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "re-sending a Sent message would deliver twice");
        (await admin.PostAsync($"/api/v1/admin/outbox/{pending}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a Pending message is already going to be attempted");
        (await admin.PostAsync($"/api/v1/admin/outbox/{Guid.CreateVersion7()}/replay", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var replayed = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == failed);
        replayed.SyncStatus.Should().Be(OutboxSyncStatus.Pending);
        replayed.ProcessedAt.Should().BeNull();

        var untouched = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == sent);
        untouched.SyncStatus.Should().Be(OutboxSyncStatus.Sent);
    }

    [Fact]
    public async Task SCR_723_reports_whether_a_real_ERP_transport_exists_at_all()
    {
        var admin = await AdminAsync();

        var monitor = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/erp-sync");

        monitor.GetProperty("transportConfigured").GetBoolean().Should().BeFalse(
            "the test host registers the logging stand-in, and a monitor that could not tell would be lying");

        var counts = monitor.GetProperty("counts");
        foreach (var status in Enum.GetNames<Domain.Awards.ErpSyncStatus>())
        {
            counts.TryGetProperty(status, out _).Should().BeTrue($"{status} must be reported even at zero");
        }

        (await admin.GetAsync("/api/v1/admin/erp-sync?status=Syncd")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.GetAsync("/api/v1/admin/erp-sync?status=Failed")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: a recognised value is accepted");
    }

    [Fact]
    public async Task SCR_726_reports_the_policy_that_is_actually_enforced_and_no_secrets()
    {
        var admin = await AdminAsync();

        var posture = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/security");

        var password = posture.GetProperty("password");
        password.GetProperty("minimumLength").GetInt32().Should().Be(12, "SECURITY-ARCHITECTURE §1.4");
        password.GetProperty("requireNonAlphanumeric").GetBoolean().Should().BeFalse(
            "NIST 800-63B: length over composition, and the screen reports it as a decision");

        posture.GetProperty("lockout").GetProperty("maxFailedAttempts").GetInt32().Should().Be(5);
        posture.GetProperty("session").GetProperty("clockSkewSeconds").GetInt32().Should().Be(30,
            "the skew is why a 15-minute token is not one; Program.cs and this handler read one key");

        posture.GetProperty("mfaRequiredRoles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain(Roles.SystemAdmin, "NFR-SEC-003 at minimum");

        posture.GetProperty("rateLimits").EnumerateArray().Should().NotBeEmpty();

        var raw = posture.GetRawText();
        foreach (var forbidden in new[] { "SigningKey", "signingKey", "ConnectionString", "connectionString", "Password\":\"", "secret" })
        {
            raw.Should().NotContain(forbidden, "the security screen must never carry a secret");
        }
    }

    [Fact]
    public async Task SCR_725_reports_the_upload_rules_the_upload_path_actually_applies()
    {
        var admin = await AdminAsync();

        var storage = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/storage");

        storage.GetProperty("maxUploadBytes").GetInt64().Should()
            .Be(Infrastructure.Storage.FileTypeSniffer.MaxSizeBytes);

        var allowed = storage.GetProperty("allowedTypes");
        foreach (var (extension, contentType) in Infrastructure.Storage.FileTypeSniffer.AllowedExtensionToContentType)
        {
            allowed.GetProperty(extension).GetString().Should().Be(contentType);
        }

        storage.GetProperty("pendingScanCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Nobody_without_admin_permission_reaches_any_of_it()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.GetAsync("/api/v1/admin/erp-sync")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.GetAsync("/api/v1/admin/security")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
                $"{role} must not be able to read the deployment's security policy");
            (await staff.GetAsync("/api/v1/admin/storage")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.PostAsync("/api/v1/admin/jobs/outbox-dispatch/trigger", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} must not be able to run platform jobs");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Ops Outsider Co");
        (await supplier.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the payloads on this screen are integration data");

        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/jobs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/outbox")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/erp-sync")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/security")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/storage")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
