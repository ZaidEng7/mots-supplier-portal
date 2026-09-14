// The real mail transport.
//
// Durability and retry are unchanged and are not this class's job: the framework already retries a job whose
// method throws. So this sender's only responsibility is to deliver, and to throw on failure rather than swallow
// it, so a transient outage produces a retry instead of a silently lost email.
//
//
// A FAILURE MUST NOT CARRY THE RECIPIENT INTO THE LOGS
//
// A mail server's rejection text commonly embeds the address it rejected. That is exactly what the privacy rule
// exists to keep out of the log stream, and the logging stand-in never had a failure path to worry about at all.
//
// So a failure is caught, logged with only the user identifier and the subject, and rethrown as a dedicated
// exception that carries neither the address nor the body.
//
// Inside the catch, the exception OBJECT is never passed to the logger: doing so logs its message, and the
// formatter includes it. Only the exception's type name is logged, as a plain string.
//
// The dedicated exception also deliberately does not wrap the original as an inner exception, because the
// language's default formatting includes an inner exception's message wherever this one is logged. Only the
// failing type's name is kept. The framework retries on it like any other thrown exception, and the original is
// still fully logged, with the same redaction, at the catch site for anyone who needs the underlying error.

namespace MotsSupplierPortal.Infrastructure.Email;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MotsSupplierPortal.Application.Common;

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
            logger.LogError(
                "Email delivery failed for user {UserId} | template {EmailSubject} | {ExceptionType}",
                userId, subject, ex.GetType().Name);
            throw new EmailDeliveryException(userId, subject, ex);
        }
    }
}

public sealed class EmailDeliveryException(Guid userId, string subject, Exception inner)
    : Exception($"Email delivery failed for user {userId}, template \"{subject}\" ({inner.GetType().Name}).")
{
}
