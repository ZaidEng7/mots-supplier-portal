using System.Text.RegularExpressions;
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
/// MSP-89, the half the reflection test cannot reach.
///
/// <para><c>EmailJobArgumentTests</c> proves no job method can <i>accept</i> a token. It says nothing
/// about whether a job, handed an id, mints the right token for the right user and sends it to the
/// right address - and that is where MSP-89 moved the work to. That behaviour was verified once by
/// hand against a running dev instance, which is the weakest evidence in the change and the only
/// evidence for the half that does the job.</para>
///
/// <para><b>The load-bearing assertion is that the token in the email resolves back to the intended
/// user.</b> Asserting "an email was sent" passes against a job that mails the wrong person's token.
/// Asserting "the body contains a token" passes against one that mints for the wrong user. So the
/// token is pulled out of the sent body and CONSUMED through the real service: consuming returns the
/// user id it is bound to, which is the one fact a wrong implementation cannot fake.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EmailJobBehaviourTests(PostgresApiFixture fixture)
{
    /// <summary>Captures sends instead of delivering them. The production IEmailSender is a logger
    /// stub until EPIC-15, so nothing is being faked that would otherwise be real - this only makes
    /// the send inspectable.</summary>
    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(Guid UserId, string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((userId, toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static string? TokenIn(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-]+)");
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private sealed record Harness(IServiceScope Scope, EmailJobs Jobs, CapturingEmailSender Sender, ISecurityTokenService Tokens);

    private Harness CreateJobs()
    {
        var scope = fixture.Services.CreateScope();
        var sender = new CapturingEmailSender();
        var tokens = scope.ServiceProvider.GetRequiredService<ISecurityTokenService>();

        var jobs = new EmailJobs(
            sender,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            tokens,
            scope.ServiceProvider.GetRequiredService<IConfiguration>());

        return new Harness(scope, jobs, sender, tokens);
    }

    private async Task<(Guid UserId, string Email)> SeedUserAsync()
    {
        var (_, email) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(
            fixture, $"Jobs {Guid.NewGuid():N}"[..18]);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return (userId, email);
    }

    [Theory]
    [InlineData(nameof(SecurityTokenPurpose.EmailVerification))]
    [InlineData(nameof(SecurityTokenPurpose.PasswordReset))]
    public async Task The_token_in_the_email_resolves_back_to_the_user_the_job_was_given(string purposeName)
    {
        var purpose = Enum.Parse<SecurityTokenPurpose>(purposeName);
        var (userId, email) = await SeedUserAsync();

        var harness = CreateJobs();
        using (harness.Scope)
        {
            if (purpose == SecurityTokenPurpose.EmailVerification)
            {
                await harness.Jobs.SendVerificationEmailAsync(userId, CancellationToken.None);
            }
            else
            {
                await harness.Jobs.SendPasswordResetEmailAsync(userId, CancellationToken.None);
            }

            var sent = harness.Sender.Sent.Should().ContainSingle().Subject;
            sent.To.Should().Be(email, "the address is resolved from the id, and resolving the wrong " +
                "user would send a working credential to somebody else");

            var rawToken = TokenIn(sent.Body);
            rawToken.Should().NotBeNull("the link is useless without one");

            // The assertion that carries the information. Consuming returns the user the token is
            // bound to; a job minting for the wrong user produces a body that still looks perfectly
            // well-formed and fails only here.
            var consumed = await harness.Tokens.ConsumeAsync(rawToken!, purpose, CancellationToken.None);

            consumed.Should().BeOfType<ConsumeSecurityTokenResult.Success>()
                .Which.UserId.Should().Be(userId,
                    "the token in the email must belong to the user the job was asked to mail");
        }
    }

    [Fact]
    public async Task A_verification_token_is_not_accepted_as_a_password_reset()
    {
        // The purposes are passed as literals inside the job, so a copy-paste between the two
        // methods would be invisible to every other assertion here - both would still mint a token,
        // send it, and resolve to the right user.
        var (userId, _) = await SeedUserAsync();

        var harness = CreateJobs();
        using (harness.Scope)
        {
            await harness.Jobs.SendVerificationEmailAsync(userId, CancellationToken.None);
            var rawToken = TokenIn(harness.Sender.Sent.Single().Body)!;

            var consumed = await harness.Tokens.ConsumeAsync(
                rawToken, SecurityTokenPurpose.PasswordReset, CancellationToken.None);

            consumed.Should().BeOfType<ConsumeSecurityTokenResult.InvalidOrExpired>(
                "a verification link that could also reset a password would be a privilege escalation");
        }
    }

    [Fact]
    public async Task An_invite_mints_an_invite_token_for_the_invited_user()
    {
        var (userId, email) = await SeedUserAsync();

        var harness = CreateJobs();
        using (harness.Scope)
        {
            await harness.Jobs.SendSupplierUserInviteEmailAsync(userId, CancellationToken.None);

            var sent = harness.Sender.Sent.Should().ContainSingle().Subject;
            sent.To.Should().Be(email);

            var consumed = await harness.Tokens.ConsumeAsync(
                TokenIn(sent.Body)!, SecurityTokenPurpose.SupplierUserInvite, CancellationToken.None);

            consumed.Should().BeOfType<ConsumeSecurityTokenResult.Success>().Which.UserId.Should().Be(userId);
        }
    }

    [Fact]
    public async Task A_job_for_a_user_who_no_longer_exists_sends_nothing_and_mints_nothing()
    {
        // Deliberate silence, per EmailJobs: a user deleted between enqueue and send is not worth
        // retrying to exhaustion. Asserted because "does nothing quietly" and "throws" look the same
        // from outside a Hangfire worker until the failed-jobs list fills up - and because a job
        // that minted a token for a missing user would leave a credential behind with no owner.
        var missingUserId = Guid.CreateVersion7();

        var harness = CreateJobs();
        using (harness.Scope)
        {
            await harness.Jobs.SendVerificationEmailAsync(missingUserId, CancellationToken.None);

            harness.Sender.Sent.Should().BeEmpty();

            var db = harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokensForGhost = await db.SecurityTokens.CountAsync(t => t.UserId == missingUserId);
            tokensForGhost.Should().Be(0, "no user, no credential");
        }
    }

    [Fact]
    public async Task No_job_in_the_real_store_carries_an_address_or_a_token()
    {
        // The store-level evidence, as a test rather than a paste. MSP-89 was verified by reading
        // hangfire.job.arguments by hand against a dev instance; that proved it once, on one day,
        // for the two paths I happened to exercise.
        //
        // The integration host runs real Hangfire against the real Postgres, so this asserts over
        // whatever the whole suite enqueued - every registration, invite, review decision and expiry
        // run the other tests performed. Deliberately global rather than scoped to one supplier: the
        // claim is that NOTHING reaches the store, and scoping it to rows I created myself would
        // exempt exactly the enqueue site somebody forgets to convert.
        await SupplierTestClient.CreateVerifiedSupplierAsync(fixture, $"Store {Guid.NewGuid():N}"[..18]);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offenders = await db.Database
            .SqlQuery<string>($@"SELECT arguments::text AS ""Value"" FROM hangfire.job
                                 WHERE arguments::text LIKE '%@%' OR arguments::text LIKE '%token=%'")
            .ToListAsync();

        offenders.Should().BeEmpty(
            "Hangfire persists arguments as plaintext for the whole retention window - MSP-87 read " +
            "15 suppliers' addresses and a working password-reset token straight out of these rows");
    }

    [Fact]
    public async Task Document_emails_resolve_the_filename_rather_than_being_told_it()
    {
        // The filename stopped travelling as an argument, so this proves the replacement works
        // rather than that the argument is gone - the reflection test already covers the latter.
        var (userId, email) = await SeedUserAsync();

        Guid documentId;
        var fileName = $"resolved-{Guid.NewGuid():N}.pdf";

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.Users.Where(u => u.Id == userId).Select(u => u.SupplierId!.Value).SingleAsync();
            var typeId = await db.DocumentTypes.Select(t => t.Id).FirstAsync();
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

            var document = Domain.Suppliers.SupplierDocument.CreatePendingScan(
                $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
                supplierId, typeId, 1, "quarantine/key", fileName, "application/pdf", 1024,
                Guid.CreateVersion7(), issueDate: null, expiryDate: today.AddDays(40),
                expiryTracked: true, today: today);

            db.SupplierDocuments.Add(document);
            await db.SaveChangesAsync();
            documentId = document.Id;
        }

        var harness = CreateJobs();
        using (harness.Scope)
        {
            await harness.Jobs.SendDocumentExpiringEmailAsync(userId, documentId, CancellationToken.None);

            var sent = harness.Sender.Sent.Should().ContainSingle().Subject;
            sent.To.Should().Be(email);
            sent.Body.Should().Contain(fileName,
                "the supplier needs to know which document, and the job now reads that from the row");
        }
    }
}
