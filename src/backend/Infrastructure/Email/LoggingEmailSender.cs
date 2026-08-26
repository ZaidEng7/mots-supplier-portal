using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Dev-only stand-in transport: logs instead of sending. Swapped for a real provider
/// (SMTP/SES) when EPIC-15 lands; the durable-job queuing around it does not change.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL to {ToEmail} | {Subject}\n{Body}", toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
