namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// Email transport abstraction. The real SMTP/SES provider is EPIC-15 (Notifications) scope;
/// this interface lets the durable job queue exist and be tested now, with delivery stubbed.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
