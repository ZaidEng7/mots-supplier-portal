using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Dev-only stand-in transport: logs that a send happened instead of sending. Swapped for a real
/// provider (SMTP/SES) when EPIC-15 lands; the durable-job queuing around it does not change.
///
/// MSP-61: this deliberately never logs the message body, at any level. Verification and
/// password-reset bodies carry live single-use tokens in their URLs, so logging the body put
/// account-takeover material into the log stream - which in production reaches a far wider
/// audience than the recipient's mailbox. Subject is logged as a template identifier for
/// traceability; body is dropped entirely rather than redacted, because the token is embedded
/// mid-URL inside free text and name-based redaction (RedactingEnricher) cannot see into values.
///
/// MSP-93/BRULE-091: for the same reason, the recipient is logged as {UserId}, not the email
/// address. BRULE-091 bars personal/sensitive data from logs outright, and an email address is
/// PII on its own - not just when paired with a token. The user id is a perfectly usable
/// identifier for tracing which send this log line is about; resolving it to an address, if ever
/// needed, is a deliberate join against the Users table, not a free read off the log stream.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Email dispatched to user {UserId} | template {EmailSubject} | {BodyLength} chars (body not logged: may contain single-use tokens)",
            userId, subject, htmlBody.Length);

        return Task.CompletedTask;
    }
}
