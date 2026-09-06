using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-076. The 23 transactional emails, admin-editable, and the token contract that makes it safe.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EmailTemplateOverrideTests(PostgresApiFixture fixture)
{
    private Task<HttpClient> AdminAsync() => StaffTestClient.CreateWithMfaAsync(fixture, Roles.SystemAdmin);

    [Fact]
    public void Every_declared_required_token_actually_appears_in_the_shipped_copy()
    {
        // The contract checked against reality. A required token the shipped wording does not contain is a
        // rule no administrator could satisfy - they would be refused for removing something that was never
        // there. Possible to check at all only because the catalogue recovers the shipped templates from
        // EmailTemplates itself rather than transcribing them.
        foreach (var definition in EmailTemplateKeys.All)
        {
            var shipped = EmailTemplateCatalogue.ShippedFor(definition.Key);
            foreach (var token in definition.RequiredTokens)
            {
                var placeholder = EmailTemplateCatalogue.Placeholder(token);
                shipped.BodyAr.Should().Contain(placeholder, $"{definition.Key} declares {token} required");
                shipped.BodyEn.Should().Contain(placeholder, $"{definition.Key} declares {token} required");
            }
        }
    }

    [Fact]
    public void Every_catalogue_key_has_shipped_copy_in_both_languages()
    {
        // Non-vacuity, and the thing a new template would silently miss: a key in EmailTemplateKeys with no
        // entry in the catalogue would throw on the admin screen rather than fail here.
        EmailTemplateKeys.All.Should().HaveCountGreaterThan(20);

        foreach (var definition in EmailTemplateKeys.All)
        {
            EmailTemplateCatalogue.Knows(definition.Key).Should().BeTrue($"{definition.Key} needs shipped copy");
            var shipped = EmailTemplateCatalogue.ShippedFor(definition.Key);
            shipped.SubjectAr.Should().NotBeNullOrWhiteSpace();
            shipped.SubjectEn.Should().NotBeNullOrWhiteSpace();
            shipped.BodyAr.Should().NotBe(shipped.BodyEn, "an Arabic body identical to the English one is untranslated");
        }
    }

    [Fact]
    public async Task The_list_shows_every_template_with_its_shipped_wording_before_anyone_edits_one()
    {
        var admin = await AdminAsync();

        var rows = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/email-templates/");

        // Driven by the catalogue, not the override table: an administrator has to be able to discover which
        // emails exist, and a table-driven list would start empty.
        rows.EnumerateArray().Should().HaveCount(EmailTemplateKeys.All.Length);
        var verification = rows.EnumerateArray().First(r => r.GetProperty("key").GetString() == EmailTemplateKeys.Verification);
        verification.GetProperty("override").ValueKind.Should().Be(JsonValueKind.Null);
        verification.GetProperty("shipped").GetProperty("bodyEn").GetString().Should().Contain("{verifyUrl}");
        verification.GetProperty("requiredTokens").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("verifyUrl");
    }

    [Fact]
    public async Task An_override_that_drops_a_required_token_is_refused_and_names_it()
    {
        var admin = await AdminAsync();

        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.Verification}", new
        {
            subjectAr = "تفعيل الحساب",
            subjectEn = "Verify your account",
            bodyAr = "<p>مرحباً بك.</p>",
            bodyEn = "<p>Welcome aboard.</p>",
        });

        // This is the whole point of T-076 being separate from T-061. That body is valid HTML, reads
        // perfectly, and would lock every new applicant out of the account they are creating - with nothing
        // in the system able to tell, because the send succeeds.
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // Read off the PROBLEM document, because §7 says every non-2xx is one. An earlier version of this
        // endpoint returned an anonymous { error, tokens } body and the middleware reshaped it away - this
        // test is what caught that.
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("MISSING_REQUIRED_TOKENS");
        problem.GetProperty("type").GetString().Should().EndWith("/errors/validation");
        var named = problem.GetProperty("tokens").EnumerateArray().Select(x => x.GetString()).ToList();
        named.Should().Contain("ar:verifyUrl").And.Contain("en:verifyUrl",
            "per locale, because 'a token is missing' is not something anyone can act on");
    }

    [Fact]
    public async Task An_override_that_keeps_the_token_is_accepted_and_reaches_the_send_path()
    {
        var admin = await AdminAsync();

        var saved = await admin.PutAsJsonAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.PasswordReset}", new
        {
            subjectAr = "إعادة تعيين كلمة المرور - صياغة الوزارة",
            subjectEn = "Reset your password - ministry wording",
            bodyAr = "<p>الرابط: {resetUrl}</p>",
            bodyEn = "<p>Ministry wording. Link: {resetUrl}</p>",
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        // Asserted through the COPY SOURCE the send path uses, not through the admin read. An override the
        // admin screen can see and the send path ignores is the "live but inert" failure this whole batch is
        // about, and it would pass an admin-side assertion.
        await using var scope = fixture.Services.CreateAsyncScope();
        var copySource = scope.ServiceProvider.GetRequiredService<IEmailCopySource>();

        var (subjectEn, bodyEn) = await copySource.ComposeAsync(
            EmailTemplateKeys.PasswordReset, "en",
            new Dictionary<string, string> { ["resetUrl"] = "https://example.test/reset?token=abc" },
            () => ("SHIPPED SUBJECT", "SHIPPED BODY"),
            CancellationToken.None);

        subjectEn.Should().Be("Reset your password - ministry wording");
        bodyEn.Should().Contain("https://example.test/reset?token=abc", "the token has to be interpolated, not echoed");
        bodyEn.Should().NotContain("{resetUrl}");

        // The Arabic half is a separate row's worth of wording and must not fall back to English.
        var (subjectAr, _) = await copySource.ComposeAsync(
            EmailTemplateKeys.PasswordReset, "ar",
            new Dictionary<string, string> { ["resetUrl"] = "https://example.test/reset?token=abc" },
            () => ("SHIPPED SUBJECT", "SHIPPED BODY"),
            CancellationToken.None);
        subjectAr.Should().Contain("صياغة الوزارة");

        // And removing it restores the shipped copy - asserted through the same seam, since that is the one
        // that decides what a recipient reads.
        (await admin.DeleteAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.PasswordReset}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var afterScope = fixture.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<IEmailCopySource>();
        var (revertedSubject, _) = await after.ComposeAsync(
            EmailTemplateKeys.PasswordReset, "en", new Dictionary<string, string>(),
            () => ("SHIPPED SUBJECT", "SHIPPED BODY"), CancellationToken.None);
        revertedSubject.Should().Be("SHIPPED SUBJECT");
    }

    [Fact]
    public async Task A_token_the_payload_cannot_fill_is_refused()
    {
        var admin = await AdminAsync();

        // D-34 applied to email: a token nobody declared reaches the recipient as the literal characters
        // {supplierName} mid-sentence, and cannot be diagnosed from the sent mail.
        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.ApplicationApproved}", new
        {
            subjectAr = "تمت الموافقة",
            subjectEn = "Approved",
            bodyAr = "<p>مرحباً {supplierName}</p>",
            bodyEn = "<p>Hello {supplierName}</p>",
        });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("UNKNOWN_TOKENS");
        problem.GetProperty("tokens").EnumerateArray().Select(x => x.GetString()).Should().Contain("supplierName");

        // Control: the same wording without the invented token is accepted, so the refusal is about the token
        // and not about the template being uneditable.
        (await admin.PutAsJsonAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.ApplicationApproved}", new
        {
            subjectAr = "تمت الموافقة",
            subjectEn = "Approved",
            bodyAr = "<p>مرحباً</p>",
            bodyEn = "<p>Hello</p>",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await admin.DeleteAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.ApplicationApproved}");
    }

    [Fact]
    public async Task An_unknown_template_key_is_a_404_and_only_an_administrator_may_write()
    {
        var admin = await AdminAsync();
        var body = new { subjectAr = "أ", subjectEn = "a", bodyAr = "<p>أ</p>", bodyEn = "<p>a</p>" };

        (await admin.PutAsJsonAsync("/api/v1/admin/email-templates/email.not_a_template", body))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        foreach (var role in new[] { Roles.ProcurementOfficer, Roles.OnboardingReviewer })
        {
            var staff = await StaffTestClient.CreateAsync(fixture, role);
            (await staff.GetAsync("/api/v1/admin/email-templates/")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await staff.PutAsJsonAsync($"/api/v1/admin/email-templates/{EmailTemplateKeys.StaffInvite}", body))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                    $"{role} must not be able to reword the invitation emails the ministry sends");
        }

        // Control.
        (await admin.GetAsync("/api/v1/admin/email-templates/")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
