using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Notifications;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Notifications;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-061/FR-ADM-007/SCR-715. The 29 notification texts were a compiled catalogue, so rewording one
/// was a redeploy.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationTemplateTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    /// <summary>Templates are global rows. Every test here puts the table back.</summary>
    private async Task ClearAsync(string type)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<NotificationTemplate>().Where(t => t.Type == type).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Every_type_the_system_can_send_is_listed_with_its_shipped_words()
    {
        var admin = await AdminAsync();

        var body = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/notification-templates");
        var items = body.EnumerateArray().ToList();

        items.Select(i => i.GetProperty("type").GetString())
            .Should().BeEquivalentTo(NotificationCatalogue.Types,
                "the screen lists what the system can send, not what happens to have been reworded");

        var one = items.First(i => i.GetProperty("type").GetString() == NotificationTypes.RfqApproved);
        var shipped = NotificationCatalogue.For(NotificationTypes.RfqApproved);
        one.GetProperty("titleAr").GetString().Should().Be(shipped.TitleAr);
        one.GetProperty("shippedTitleAr").GetString().Should().Be(shipped.TitleAr);
        one.GetProperty("isOverridden").GetBoolean().Should().BeFalse();

        // The screen has to be able to say which tokens are available without a second copy of the
        // catalogue, and the set is derived from the shipped copy's own text.
        one.GetProperty("availableTokens").EnumerateArray().Select(t => t.GetString())
            .Should().BeEquivalentTo(NotificationCatalogue.TokensFor(NotificationTypes.RfqApproved));
    }

    [Fact]
    public async Task An_override_is_what_a_supplier_actually_receives()
    {
        var admin = await AdminAsync();
        const string type = NotificationTypes.RfqApproved;
        try
        {
            var tokens = NotificationCatalogue.TokensFor(type);
            // Uses a token the type really carries, so the assertion below proves interpolation still
            // happens through the override rather than only that the words changed.
            var token = tokens.Contains("rfqCode") ? "{rfqCode}" : string.Empty;

            (await admin.PutAsJsonAsync($"/api/v1/admin/notification-templates/{type}", new
            {
                titleAr = "عنوان معاد صياغته",
                titleEn = "A reworded title",
                bodyAr = $"نص معاد صياغته {token}",
                bodyEn = $"A reworded body {token}",
            })).EnsureSuccessStatusCode();

            // Asserted through the source the writer uses, not through the admin read - a template a
            // screen displays but the notification writer ignores is the failure this replaces.
            await using var scope = fixture.Services.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<INotificationCopySource>();
            var entry = await source.ForAsync(type, default);
            entry.TitleEn.Should().Be("A reworded title");

            var rendered = NotificationCatalogue.Render(entry, new Dictionary<string, string?> { ["rfqCode"] = "RFQ-2026-000123" });
            if (token.Length > 0)
            {
                rendered.BodyEn.Should().Contain("RFQ-2026-000123");
                rendered.BodyEn.Should().NotContain("{rfqCode}", "an override interpolates exactly as the shipped copy does");
            }

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AuditLogs.AsNoTracking()
                .AnyAsync(a => a.AggregateType == "NotificationTemplate"
                    && a.Action == "notification.template.updated"
                    && a.ReferenceCode == type))
                .Should().BeTrue("\"who reworded the award notice\" is a governance question");
        }
        finally
        {
            await ClearAsync(type);
        }
    }

    [Fact]
    public async Task A_token_the_type_cannot_fill_is_refused_and_named()
    {
        var admin = await AdminAsync();
        const string type = NotificationTypes.RfqApproved;

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/notification-templates/{type}", new
        {
            titleAr = "عنوان",
            titleEn = "Title",
            bodyAr = "السعر {price} والبريد {email}",
            bodyEn = "The price is {price} and {email}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Named, because an unfillable token reaches the supplier as the literal characters {price}
        // and cannot be diagnosed from the notification row.
        problem.GetProperty("tokens").EnumerateArray().Select(t => t.GetString())
            .Should().BeEquivalentTo(["email", "price"]);

        // And nothing was stored.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<NotificationTemplate>().CountAsync(t => t.Type == type)).Should().Be(0);
    }

    [Fact]
    public async Task A_template_that_drops_a_token_is_accepted()
    {
        // The control for the test above: the rule is a subset, not an exact match. An administrator
        // may write copy that says less than the shipped copy did.
        var admin = await AdminAsync();
        const string type = NotificationTypes.RfqApproved;
        try
        {
            (await admin.PutAsJsonAsync($"/api/v1/admin/notification-templates/{type}", new
            {
                titleAr = "عنوان بدون رموز",
                titleEn = "A title with no tokens",
                bodyAr = "نص بدون رموز",
                bodyEn = "A body with no tokens",
            })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await ClearAsync(type);
        }
    }

    [Fact]
    public async Task Reverting_restores_the_shipped_copy_and_is_idempotent()
    {
        var admin = await AdminAsync();
        const string type = NotificationTypes.RfqApproved;
        var shipped = NotificationCatalogue.For(type);

        (await admin.PutAsJsonAsync($"/api/v1/admin/notification-templates/{type}", new
        {
            titleAr = "مؤقت", titleEn = "Temporary", bodyAr = "مؤقت", bodyEn = "Temporary",
        })).EnsureSuccessStatusCode();

        var reverted = await admin.DeleteAsync($"/api/v1/admin/notification-templates/{type}");
        reverted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reverted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("titleEn").GetString().Should().Be(shipped.TitleEn);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<NotificationTemplate>().CountAsync(t => t.Type == type)).Should().Be(0);

        // Reverting a type nobody overrode asks for an outcome that is already true.
        (await admin.DeleteAsync($"/api/v1/admin/notification-templates/{type}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_type_this_system_never_sends_is_not_a_resource()
    {
        var admin = await AdminAsync();

        (await admin.PutAsJsonAsync("/api/v1/admin/notification-templates/invented.type", new
        {
            titleAr = "عنوان", titleEn = "Title", bodyAr = "نص", bodyEn = "Body",
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.DeleteAsync("/api/v1/admin/notification-templates/invented.type"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Both_locales_are_required()
    {
        // An Arabic title with no English one renders blank for an English-language user, and this
        // product's fallback is Arabic-first rather than empty - so the refusal belongs at the write.
        var admin = await AdminAsync();

        (await admin.PutAsJsonAsync($"/api/v1/admin/notification-templates/{NotificationTypes.RfqApproved}", new
        {
            titleAr = "عنوان", titleEn = "", bodyAr = "نص", bodyEn = "Body",
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Only_an_administrator_can_reword_a_notification()
    {
        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.ProcurementManager, Roles.MinistryViewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/notification-templates")).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{role} does not hold reference.manage");
            (await staff.DeleteAsync($"/api/v1/admin/notification-templates/{NotificationTypes.RfqApproved}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var supplier = await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, "Template Outsider");
        (await supplier.GetAsync("/api/v1/admin/notification-templates")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The control.
        var admin = await AdminAsync();
        (await admin.GetAsync("/api/v1/admin/notification-templates")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
