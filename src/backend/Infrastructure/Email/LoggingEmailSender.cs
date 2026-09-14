// The stand-in transport for development: it records that a send happened instead of sending.
//
// Swapped for a real provider when one lands; the durable queuing around it does not change.
//
//
// IT NEVER LOGS THE BODY, AT ANY LEVEL
//
// Verification and password-reset bodies carry live single-use tokens in their links, so logging the body put
// account-takeover material into the log stream, which in production reaches a far wider audience than the
// recipient's mailbox.
//
// The subject is logged as a template identifier for traceability. The body is dropped entirely rather than
// redacted, because the token sits mid-link inside free text and name-based redaction cannot see into values.
//
//
// AND THE RECIPIENT IS LOGGED AS AN IDENTIFIER, NOT AN ADDRESS
//
// The rule bars personal data from logs outright, and an email address is personal data on its own rather than
// only when paired with a token.
//
// The user identifier is a perfectly usable way to trace which send a line is about, and resolving it to an
// address, if that is ever needed, is a deliberate join against the users table rather than a free read off the
// log stream.

namespace MotsSupplierPortal.Infrastructure.Email;

using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

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
