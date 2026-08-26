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
}
