namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Email transport abstraction. The real SMTP/SES provider is EPIC-15 (Notifications) scope;
/// this interface lets the durable job queue exist and be tested now, with delivery stubbed.
///
/// <para>MSP-93/BRULE-091: <paramref name="userId"/> exists so an implementation can identify the
/// send event without putting the email address itself into a log line - BRULE-091 forbids
/// personal/sensitive data in logs, and an email address is exactly that. <paramref name="toEmail"/>
/// remains the actual delivery target a real transport needs to send the message at all; the
/// requirement is that whatever a SENDER logs about the event uses <paramref name="userId"/>, not
/// this parameter.</para>
/// </summary>
public interface IEmailSender
{
    Task SendAsync(Guid userId, string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
