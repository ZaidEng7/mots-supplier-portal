using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// BRULE-023's code half. The rule - expiry of an award-critical document suspends the supplier - has been
/// live since the column was added and could never fire: no seeded type sets the flag, and nothing could set
/// it, because it was absent from the reference-data write contract and from the admin screen. A migration was
/// the only way in.
///
/// <para><b>These tests do not decide anything.</b> Which document types are award-critical is a ministry
/// judgement about procurement risk - "was suspended for a fortnight" is not undone by reactivation - so the
/// assertions are about the flag being SETTABLE and READABLE, and about the shipped default staying false.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AwardCriticalFlagTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    /// <summary>
    /// The decision half, now made. This test was written asserting that NOTHING was award-critical, so
    /// that marking a type would have to be acknowledged here rather than slipped in - the consequence
    /// being a suspended supplier. This edit is that acknowledgement.
    ///
    /// <para>D-58 marks the commercial register and the tax card: both are what make a company legally
    /// able to hold a contract, and an expired one means it cannot lawfully be awarded or paid. Chamber
    /// membership stays off - it evidences standing rather than capacity. Migration
    /// 20260908115449_AwardCriticalDocumentTypes carries the values; the end-to-end consequence is
    /// proved in <see cref="AwardCriticalBlocksBiddingTests"/>, which was the condition D-58 shipped
    /// with, because until it existed the rule had never run against a real value.</para>
    /// </summary>
    [Fact]
    public async Task Only_the_document_types_D58_names_are_award_critical()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var flags = await db.Set<DocumentType>().AsNoTracking()
            .Select(t => new { t.Code, t.IsAwardCritical }).ToListAsync();

        flags.Should().NotBeEmpty("the reference data must be seeded for this assertion to mean anything");
        flags.Where(t => t.IsAwardCritical).Select(t => t.Code)
            .Should().BeEquivalentTo(["commercial_registration", "tax_certificate"],
                "marking a type suspends live suppliers, so which ones are marked is a decision with a " +
                "record - D-58 - and not a default anybody may widen in passing");
    }

    [Fact]
    public async Task The_flag_is_on_the_read_and_null_on_tables_that_do_not_have_it()
    {
        var admin = await AdminAsync();

        var documentTypes = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/document-types");
        documentTypes.EnumerateArray().Should().NotBeEmpty();
        foreach (var item in documentTypes.EnumerateArray())
        {
            // True or False, never Null: the point of this assertion is that a table which HAS the flag
            // reports it as a boolean either way. Which rows are true is D-58's business, asserted in
            // Only_the_document_types_D58_names_are_award_critical.
            item.GetProperty("isAwardCritical").ValueKind.Should()
                .BeOneOf([JsonValueKind.False, JsonValueKind.True],
                    "the flag has to be readable, and a boolean is a different fact from absent");
        }

        // Null elsewhere, not false: "this table has no such flag" and "this row has it off" are different
        // facts, and the screen shows a toggle only where there is something to toggle.
        var currencies = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/currencies");
        foreach (var item in currencies.EnumerateArray())
        {
            item.GetProperty("isAwardCritical").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task An_administrator_can_now_record_the_decision_and_a_rename_cannot_clear_it()
    {
        var admin = await AdminAsync();
        var code = $"probe_{Guid.NewGuid():N}"[..20];

        // T-073. The probe TYPE is this test's own, but the award-critical SET is not: the expiry job
        // suspends a supplier for any type carrying the flag, and three tests assert against the set
        // as a whole. Clearing it was this test's last line, so a failing assertion left an
        // award-critical document type standing for the rest of the run.
        await using var scoped = new ClearAwardCritical(admin, code);

        var created = await admin.PostAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
        {
            nameAr = "نوع اختبار", nameEn = "Probe type", isRequired = false, expiryTracked = true,
        });
        created.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Defaults false on create, matching the migration: a new type is not award-critical until somebody
        // says so, and defaulting the other way would suspend suppliers over a type nobody had assessed.
        var afterCreate = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/document-types");
        afterCreate.EnumerateArray().First(i => i.GetProperty("code").GetString() == code)
            .GetProperty("isAwardCritical").GetBoolean().Should().BeFalse();

        var marked = await admin.PutAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
        {
            nameAr = "نوع اختبار", nameEn = "Probe type", isRequired = false, expiryTracked = true,
            isAwardCritical = true,
        });
        marked.StatusCode.Should().Be(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());

        var afterMark = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/document-types");
        afterMark.EnumerateArray().First(i => i.GetProperty("code").GetString() == code)
            .GetProperty("isAwardCritical").GetBoolean().Should().BeTrue();

        // The half that matters more than setting it: an administrator fixing an Arabic typo must not clear
        // the one flag on this screen whose effect is to suspend live suppliers. Omitted means unchanged.
        var renamed = await admin.PutAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
        {
            nameAr = "نوع اختبار معدّل", nameEn = "Probe type renamed", isRequired = false, expiryTracked = true,
        });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterRename = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/document-types");
        var row = afterRename.EnumerateArray().First(i => i.GetProperty("code").GetString() == code);
        row.GetProperty("nameEn").GetString().Should().Be("Probe type renamed");
        row.GetProperty("isAwardCritical").GetBoolean().Should().BeTrue("a rename must not clear it");

        // And it can be cleared deliberately, so the decision is reversible before an expiry acts on it.
        await admin.PutAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
        {
            nameAr = "نوع اختبار معدّل", nameEn = "Probe type renamed", isRequired = false, expiryTracked = true,
            isAwardCritical = false,
        });
        var afterClear = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reference/document-types");
        afterClear.EnumerateArray().First(i => i.GetProperty("code").GetString() == code)
            .GetProperty("isAwardCritical").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Takes the flag back off, and deactivates the probe type so it leaves the catalogue the
    /// administrator screen shows. D-28 refuses deletion, which is the right rule for a real type and
    /// the reason this is deactivation rather than a DELETE.
    /// </summary>
    private sealed class ClearAwardCritical(HttpClient admin, string code) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await admin.PutAsJsonAsync($"/api/v1/admin/reference/document-types/{code}", new
            {
                nameAr = "نوع اختبار", nameEn = "Probe type", isRequired = false, expiryTracked = true,
                isAwardCritical = false,
            });
            await admin.PostAsJsonAsync($"/api/v1/admin/reference/document-types/{code}/deactivate", new { });
        }
    }
}
