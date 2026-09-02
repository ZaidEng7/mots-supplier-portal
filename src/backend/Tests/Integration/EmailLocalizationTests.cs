using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// MSP-69: proves locale actually reaches a rendered email end-to-end against the real database -
/// not just that EmailTemplates branches correctly in isolation (EmailTemplatesTests), but that
/// EmailJobs reads the real AppUser.Language column and the registration endpoint sets it from a
/// real Accept-Language header on a real HTTP request.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EmailLocalizationTests(PostgresApiFixture fixture)
{
    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private async Task<Guid> SeedUserWithLanguageAsync(string language)
    {
        var (client, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Locale {Guid.NewGuid():N}"[..18]);
        _ = client;

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Language = language;
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(string Subject, string Body)> SendApprovedEmailAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var sender = new CapturingEmailSender();
        var jobs = new EmailJobs(
            sender,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<ISecurityTokenService>(),
            scope.ServiceProvider.GetRequiredService<IConfiguration>());

        await jobs.SendApplicationApprovedEmailAsync(userId, CancellationToken.None);
        return sender.Sent.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task An_Arabic_locale_user_receives_Arabic_content()
    {
        var userId = await SeedUserWithLanguageAsync("ar");

        var (subject, body) = await SendApprovedEmailAsync(userId);

        subject.Should().MatchRegex(@"\p{IsArabic}");
        body.Should().MatchRegex(@"\p{IsArabic}");
    }

    [Fact]
    public async Task An_English_locale_user_receives_English_content()
    {
        var userId = await SeedUserWithLanguageAsync("en");

        var (subject, body) = await SendApprovedEmailAsync(userId);

        subject.Should().NotMatchRegex(@"\p{IsArabic}");
        body.Should().NotMatchRegex(@"\p{IsArabic}");
        subject.Should().Be("Your supplier application has been approved");
    }

    [Fact]
    public async Task Registering_with_an_English_Accept_Language_header_sets_the_users_locale_to_English()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = $"EN Locale {Guid.NewGuid():N}"[..20],
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Locale Tester",
            representativePhone = "+963900000000",
            email,
            password = SupplierTestClient.Password,
        });
        response.EnsureSuccessStatusCode();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var language = await db.Users.Where(u => u.Email == email).Select(u => u.Language).SingleAsync();

        language.Should().Be("en");
    }

    [Fact]
    public async Task Registering_with_no_Accept_Language_header_defaults_the_users_locale_to_Arabic()
    {
        var client = fixture.CreateClient();
        var email = $"itest-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            displayNameAr = "شركة اختبار",
            displayNameEn = $"Default Locale {Guid.NewGuid():N}"[..20],
            registrationNumber = $"RC-{Guid.NewGuid():N}"[..12],
            representativeName = "Locale Tester",
            representativePhone = "+963900000000",
            email,
            password = SupplierTestClient.Password,
        });
        response.EnsureSuccessStatusCode();

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var language = await db.Users.Where(u => u.Email == email).Select(u => u.Language).SingleAsync();

        language.Should().Be("ar");
    }
}
