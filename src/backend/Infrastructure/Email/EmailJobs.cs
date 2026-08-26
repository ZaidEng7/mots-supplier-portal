using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Hangfire-invoked job methods. Enqueued via IBackgroundJobClient.Enqueue so the send is a
/// durable, retried background operation - never inline in the request that triggered it.
/// </summary>
public sealed class EmailJobs(IEmailSender emailSender)
{
    public Task SendVerificationEmailAsync(string toEmail, string verifyUrl, CancellationToken ct) =>
        emailSender.SendAsync(
            toEmail,
            "Verify your MOTS Supplier Portal account",
            $"<p>Click to verify your email:</p><p><a href=\"{verifyUrl}\">{verifyUrl}</a></p>",
            ct);

    public Task SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken ct) =>
        emailSender.SendAsync(
            toEmail,
            "Reset your MOTS Supplier Portal password",
            $"<p>Click to reset your password:</p><p><a href=\"{resetUrl}\">{resetUrl}</a></p>",
            ct);

    public Task SendApplicationApprovedEmailAsync(string toEmail, CancellationToken ct) =>
        emailSender.SendAsync(toEmail, "Your supplier application has been approved",
            "<p>Congratulations - your supplier application has been approved and your account is now Active.</p>", ct);

    public Task SendApplicationRejectedEmailAsync(string toEmail, string reason, CancellationToken ct) =>
        emailSender.SendAsync(toEmail, "Your supplier application was not approved",
            $"<p>Your supplier application was rejected for the following reason:</p><p>{reason}</p><p>You may correct the issue and register again.</p>", ct);

    public Task SendInfoRequestedEmailAsync(string toEmail, string reason, CancellationToken ct) =>
        emailSender.SendAsync(toEmail, "Action needed on your supplier application",
            $"<p>The reviewer has requested more information:</p><p>{reason}</p><p>Please log in to address the flagged items and resubmit.</p>", ct);

    public Task SendDocumentExpiringEmailAsync(string toEmail, string documentName, CancellationToken ct) =>
        emailSender.SendAsync(toEmail, "A document on your supplier profile is expiring soon",
            $"<p>Your document \"{documentName}\" will expire soon. Please renew and re-upload it.</p>", ct);

    public Task SendDocumentExpiredEmailAsync(string toEmail, string documentName, CancellationToken ct) =>
        emailSender.SendAsync(toEmail, "A document on your supplier profile has expired",
            $"<p>Your document \"{documentName}\" has expired and your profile is now flagged incomplete. Please re-upload it.</p>", ct);
}
