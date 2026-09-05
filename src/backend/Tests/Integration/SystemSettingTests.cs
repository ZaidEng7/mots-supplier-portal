using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-060/FR-ADM-006. Registration mode, the default currency and the two document-expiry windows were
/// a const, a seed row and two appsettings keys - which is to say, a redeploy each.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SystemSettingTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    /// <summary>Settings are global rows, so a test that leaves one set changes every later test. Each
    /// one here puts the table back.</summary>
    private async Task ClearAsync(string key)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<SystemSetting>().Where(s => s.Key == key).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task The_catalogue_lists_every_setting_including_the_ones_nobody_has_touched()
    {
        var admin = await AdminAsync();

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        var keys = settings.EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();

        keys.Should().BeEquivalentTo(SystemSettings.All.Select(d => d.Key),
            "the screen lists what CAN be configured, not what happens to have a row");

        // A fresh deployment has no rows at all, and the difference between "unset" and "an
        // administrator chose 30" is the fact the audit trail carries.
        var window = settings.EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == SystemSettings.ExpiringSoonWindowDays);
        window.GetProperty("value").GetString().Should().Be("30");
        window.GetProperty("isOverridden").GetBoolean().Should().BeFalse();
        window.GetProperty("minimum").GetInt32().Should().Be(1);
        window.GetProperty("maximum").GetInt32().Should().Be(365);
    }

    [Fact]
    public async Task A_stored_value_is_what_the_consumer_reads()
    {
        var admin = await AdminAsync();
        try
        {
            var response = await admin.PutAsJsonAsync(
                $"/api/v1/admin/settings/{SystemSettings.ExpiringSoonWindowDays}", new { value = "45" });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            // Asserted against storage AND against the reader the job uses - a settings screen that
            // stores a row nobody reads is the failure this replaces.
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<SystemSetting>().AsNoTracking()
                .Where(s => s.Key == SystemSettings.ExpiringSoonWindowDays)
                .Select(s => s.Value).FirstOrDefaultAsync())
                .Should().Be("45");

            var reader = scope.ServiceProvider
                .GetRequiredService<MotsSupplierPortal.Infrastructure.Configuration.ISystemSettingReader>();
            (await reader.GetAsync(SystemSettings.ExpiringSoonWindowDays, default)).Should().Be("45");

            // And it is audited with both values: "who widened the expiry window" is a governance
            // question, and the answer is useless without what it was before.
            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.AggregateType == "SystemSetting" && a.ReferenceCode == SystemSettings.ExpiringSoonWindowDays)
                .OrderByDescending(a => a.OccurredAt)
                .FirstAsync();
            audit.Action.Should().Be("setting.updated");
            audit.FromState.Should().Be("(unset)");
            audit.ToState.Should().Be("45");
        }
        finally
        {
            await ClearAsync(SystemSettings.ExpiringSoonWindowDays);
        }
    }

    [Fact]
    public async Task Values_outside_the_definition_are_refused_with_the_rule_that_was_broken()
    {
        var admin = await AdminAsync();

        var cases = new (string Key, string Value, string Reason)[]
        {
            (SystemSettings.ExpiringSoonWindowDays, "0", "value_out_of_range"),
            (SystemSettings.ExpiringSoonWindowDays, "400", "value_out_of_range"),
            (SystemSettings.ExpiringSoonWindowDays, "thirty", "value_out_of_range"),
            (SystemSettings.RegistrationMode, "invite-only", "value_not_allowed"),
            // A repeated rung would look accepted and behave differently: the reminder ledger keys on
            // the threshold value, so the second send is suppressed silently.
            (SystemSettings.RenewalReminderDays, "30,14,14", "value_has_duplicates"),
            // D-28 makes deactivation the normal way a code leaves the catalogue, so a default
            // currency pointing at an inactive code is a case that will occur.
            (SystemSettings.DefaultCurrencyCode, "ZZZ", "reference_code_not_active"),
        };

        foreach (var (key, value, reason) in cases)
        {
            var response = await admin.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value });
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"{key}={value}");
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("reason").GetString().Should().Be(reason);
        }

        // Nothing was stored by any of them.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<SystemSetting>().CountAsync()).Should().Be(0);

        // A key that is not in the catalogue is not a resource, and saying "your value is invalid"
        // would send the caller looking in the wrong place.
        (await admin.PutAsJsonAsync("/api/v1/admin/settings/registration.made-up", new { value = "open" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Closing_registration_shuts_the_public_front_door()
    {
        var admin = await AdminAsync();
        try
        {
            (await admin.PutAsJsonAsync($"/api/v1/admin/settings/{SystemSettings.RegistrationMode}",
                new { value = SystemSettings.RegistrationClosed })).EnsureSuccessStatusCode();

            var anonymous = fixture.CreateClient();
            var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
            {
                displayNameAr = "شركة مغلقة",
                displayNameEn = "Closed Co",
                registrationNumber = (string?)null,
                representativeName = "A Representative",
                representativePhone = "+963900000111",
                email = $"closed-{Guid.NewGuid():N}@example.com",
                password = "A-strong-passphrase-9",
            });

            var payload = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, payload);
            // §7's shape: the middleware turns `error` into the machine code and `message` into the
            // detail, so this asserts the wire contract a client actually switches on.
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().Should().Be("REGISTRATION_CLOSED");
            problem.GetProperty("detail").GetString().Should().Contain("Contact the Ministry");

            // Nothing was written. The refusal is before validation and before the handler, so a
            // closed portal does not half-create an applicant.
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Suppliers.CountAsync(s => s.DisplayNameEn == "Closed Co")).Should().Be(0);
        }
        finally
        {
            await ClearAsync(SystemSettings.RegistrationMode);
        }
    }

    [Fact]
    public async Task Registration_is_open_when_nobody_has_closed_it()
    {
        // The control for the test above, and the requirement's own default: FR-REG-002 says open.
        var anonymous = fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة مفتوحة",
            displayNameEn = "Open Co",
            registrationNumber = (string?)null,
            representativeName = "A Representative",
            representativePhone = "+963900000112",
            email = $"open-{Guid.NewGuid():N}@example.com",
            password = "A-strong-passphrase-9",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_public_read_carries_the_allow_listed_settings_and_nothing_else()
    {
        var anonymous = fixture.CreateClient();

        var body = await anonymous.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/reference/settings");

        body.Should().NotBeNull();
        body!.Keys.Should().BeEquivalentTo(SystemSettings.PubliclyReadable,
            "an allow-list, so a setting added later is invisible here until someone decides otherwise");
        body[SystemSettings.RegistrationMode].Should().Be(SystemSettings.RegistrationOpen);
        body[SystemSettings.DefaultCurrencyCode].Should().Be("SYP");
    }

    [Fact]
    public async Task Only_an_administrator_can_read_or_write_the_catalogue()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/settings")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold reference.manage");
            (await staff.PutAsJsonAsync($"/api/v1/admin/settings/{SystemSettings.RegistrationMode}",
                new { value = SystemSettings.RegistrationClosed })).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} must not be able to close registration");
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Settings Outsider");
        (await supplier.GetAsync("/api/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The control, and proof the write above was refused rather than merely unobserved.
        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<SystemSetting>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_review_target_follows_the_configured_sla()
    {
        // A-5. The SLA timer exists in BUSINESS-PROCESSES.md §5 and has no number, so this is the
        // number - configurable, defaulted to five working days, and surfaced as a target.
        var admin = await AdminAsync();
        var reviewer = await StaffTestClient.CreateAsync(fixture, Roles.OnboardingReviewer);
        Guid seededSupplierId;

        // A case has to be IN the queue for the target to mean anything. Built through the real
        // transitions, same as ReviewQueueAssignmentTests does, so the row is one production could have
        // produced - and REMOVED at the end, because the review queue is shared state and a row left in
        // it is an order dependence waiting for the row to start mattering. It did: this test's supplier
        // displaced a row that ReviewQueuePaginationTests asserts by position.
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = Supplier.Register(
                referenceCode: $"SUP-SLA-{Guid.NewGuid():N}"[..20],
                displayNameAr: "شركة اختبار",
                displayNameEn: $"Sla Test {Guid.NewGuid():N}"[..40],
                registrationNumber: null,
                primaryRepresentativeName: "Tester",
                primaryRepresentativeEmail: $"sla-{Guid.NewGuid():N}@example.com",
                primaryRepresentativePhone: "+963900000000");
            supplier.MarkEmailVerified();
            supplier.UpdateCoreProfile(null, null, null, "SYP");
            supplier.AddAddress(AddressKind.HeadOffice, "L1", null, "Damascus", "DM", "SY", null, null, null);
            supplier.LinkCategory("CAT-1", isComplianceCritical: false);
            supplier.AcceptTerms("v1");
            supplier.Submit([]);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            seededSupplierId = supplier.Id;
        }

        var beforeChange = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue");
        var firstItem = beforeChange.GetProperty("data").EnumerateArray().First();
        var enteredAt = firstItem.GetProperty("enteredQueueAt").GetDateTimeOffset();
        var defaultTarget = firstItem.GetProperty("reviewTargetAt").GetDateTimeOffset();

        defaultTarget.Should().BeAfter(enteredAt, "a target is in the future, or it is not a target");
        defaultTarget.DayOfWeek.Should().NotBe(DayOfWeek.Friday, "working days, not calendar days");
        defaultTarget.DayOfWeek.Should().NotBe(DayOfWeek.Saturday);

        try
        {
            (await admin.PutAsJsonAsync($"/api/v1/admin/settings/{SystemSettings.ReviewSlaWorkingDays}",
                new { value = "20" })).EnsureSuccessStatusCode();

            var afterChange = await reviewer.GetFromJsonAsync<JsonElement>("/api/v1/review/queue");
            var moved = afterChange.GetProperty("data").EnumerateArray().First()
                .GetProperty("reviewTargetAt").GetDateTimeOffset();

            // The control: the setting is what the queue reads, not a constant that happens to match.
            moved.Should().BeAfter(defaultTarget, "a longer SLA moves the target out");
        }
        finally
        {
            await ClearAsync(SystemSettings.ReviewSlaWorkingDays);

            await using var cleanup = fixture.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Suppliers.Where(s => s.Id == seededSupplierId).ExecuteDeleteAsync();
        }
    }
}
