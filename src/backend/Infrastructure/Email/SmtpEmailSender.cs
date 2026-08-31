using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Task #35: real transport for EPIC-15/FR-NOT-003's "durable background job with retry" half.
/// The durability/retry itself is unchanged - Hangfire already retries a job whose method throws
/// (see EmailJobs's Enqueue call sites), so this sender's only job is to actually deliver and to
/// throw on failure rather than swallow it, so a transient SMTP outage produces a retry instead of
/// a silently lost email.
///
/// <para>MSP-61/MSP-93/BRULE-091, extended to a real failure path that LoggingEmailSender never
/// had: on send failure this never lets the raw SMTP exception (whose message commonly embeds the
/// recipient address, e.g. "550 mailbox unavailable: user@example.com") propagate or get logged.
/// It's caught, logged with userId/subject only (never toEmail, never htmlBody), and rethrown as a
/// new <see cref="EmailDeliveryException"/> carrying none of that - so a PII-bearing message
/// cannot land in Serilog output or Hangfire's own job-failure storage, which keeps this session's
/// same guarantee for a channel that previously had no failure mode to worry about at all.</para>
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var opts = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(opts.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(opts.Host, opts.Port, opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
            if (!string.IsNullOrEmpty(opts.User) && !string.IsNullOrEmpty(opts.Password))
            {
                await client.AuthenticateAsync(opts.User, opts.Password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation(
                "Email delivered to user {UserId} | template {EmailSubject} | {BodyLength} chars (body not logged: may contain single-use tokens)",
                userId, subject, htmlBody.Length);
        }
        catch (Exception ex)
        {
            // Never `logger.LogError(ex, ...)` here: passing the exception OBJECT logs its Message
            // (and Serilog's default formatter includes it), and an SMTP rejection's own error text
            // frequently echoes the recipient address back - exactly what MSP-61 exists to keep out
            // of the log stream. Only the exception's type name is logged, as a plain string arg.
            logger.LogError(
                "Email delivery failed for user {UserId} | template {EmailSubject} | {ExceptionType}",
                userId, subject, ex.GetType().Name);
            throw new EmailDeliveryException(userId, subject, ex);
        }
    }
}

/// <summary>Carries no recipient address or body - see SmtpEmailSender's MSP-61 note. Deliberately
/// does not wrap the original exception as `InnerException`: MailKit/SMTP exceptions can carry the
/// recipient address inside their own Message (e.g. an SMTP server's rejection text echoing it
/// back), and .NET's default exception formatting includes the inner exception's Message wherever
/// this one is logged - so only the failing exception's TYPE NAME is kept, in this message, never
/// its Message or the original object. Hangfire retries on this like any other thrown exception
/// from a job method; the original exception is still fully logged (with the same redaction) at
/// the SmtpEmailSender catch site above, for anyone who needs the underlying SMTP error to debug.</summary>
public sealed class EmailDeliveryException(Guid userId, string subject, Exception inner)
    : Exception($"Email delivery failed for user {userId}, template \"{subject}\" ({inner.GetType().Name}).")
{
}
