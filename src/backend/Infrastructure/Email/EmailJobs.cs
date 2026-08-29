using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Hangfire-invoked job methods. Enqueued via IBackgroundJobClient.Enqueue so the send is a
/// durable, retried background operation - never inline in the request that triggered it.
///
/// <para><b>Every argument here is an identifier, and that is a security property rather than a
/// style.</b> Hangfire persists job arguments as plaintext JSON in its own tables, and those rows
/// outlive the emails they produced - succeeded jobs are retained on a timer. MSP-87 found a
/// supplier_admin reading 15 other suppliers' email addresses out of that store, together with live
/// verification and password-reset URLs; the token in one of them was a working credential.</para>
///
/// <para>MSP-87 restricted the dashboard. That is a rule about who may look. This is the other half:
/// there is nothing there to read. The rule protects one surface, the absence protects every surface
/// - backups, replicas, a support query, log aggregation, and whatever gets built over the job data
/// later. NFR-PRIV-004 reasons about PII in logs, and the Hangfire store is far closer to a log than
/// to the audit table.</para>
///
/// <para><b>Tokens are issued here, not passed in.</b> A token baked into a job argument is a
/// credential at rest for the whole retention window. Issuing at send time also shortens the window
/// in which the token is useful, and a retry minting a fresh one is correct rather than a
/// side effect - the previous one expires on its own.</para>
/// </summary>
public sealed class EmailJobs(
    IEmailSender emailSender,
    AppDbContext db,
    ISecurityTokenService securityTokenService,
    IConfiguration configuration)
{
    private async Task<string?> EmailForAsync(Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Resolves the recipient and sends, or does nothing if the user has gone.
    ///
    /// Silence is deliberate. A user deleted between enqueue and send is not an error worth
    /// retrying - Hangfire would retry it to exhaustion and then surface a failed job that needs a
    /// human to dismiss. The send is best-effort by construction; the durable record of what
    /// happened is the audit row the handler already wrote.
    /// </summary>
    private async Task SendToUserAsync(Guid userId, Func<string, (string Subject, string Body)> compose, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var (subject, body) = compose(email);
        await emailSender.SendAsync(email, subject, body, ct);
    }

    private string PublicUrl => configuration["App:PublicUrl"]
        ?? throw new InvalidOperationException("App:PublicUrl is not configured.");

    // ---- token-bearing emails -------------------------------------------------------------
    //
    // SECURITY-ARCHITECTURE.md §1.6: the link carries only the opaque token, never the user id -
    // the token alone resolves the user. That still holds; what changed is that the token is now
    // minted inside the job instead of being handed to it.

    public async Task SendVerificationEmailAsync(Guid userId, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), ct);
        var verifyUrl = $"{PublicUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        await emailSender.SendAsync(email, "Verify your MOTS Supplier Portal account",
            $"<p>Click to verify your email:</p><p><a href=\"{verifyUrl}\">{verifyUrl}</a></p>", ct);
    }

    public async Task SendPasswordResetEmailAsync(Guid userId, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.PasswordReset, TimeSpan.FromMinutes(30), ct);
        var resetUrl = $"{PublicUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        await emailSender.SendAsync(email, "Reset your MOTS Supplier Portal password",
            $"<p>Click to reset your password:</p><p><a href=\"{resetUrl}\">{resetUrl}</a></p>", ct);
    }

    public async Task SendSupplierUserInviteEmailAsync(Guid userId, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.SupplierUserInvite, TimeSpan.FromDays(7), ct);
        var acceptUrl = $"{PublicUrl}/accept-invite?token={Uri.EscapeDataString(rawToken)}";

        await emailSender.SendAsync(email, "You've been invited to the MOTS Supplier Portal",
            "<p>You've been invited to join your organization's supplier account. Click to set your " +
            $"password and get started:</p><p><a href=\"{acceptUrl}\">{acceptUrl}</a></p>", ct);
    }

    // ---- application lifecycle ------------------------------------------------------------

    public Task SendApplicationApprovedEmailAsync(Guid userId, CancellationToken ct) =>
        SendToUserAsync(userId, _ => (
            "Your supplier application has been approved",
            "<p>Congratulations - your supplier application has been approved and your account is now Active.</p>"), ct);

    /// <summary>
    /// The one remaining free-text argument, and it is stated rather than quietly kept.
    ///
    /// <para>The rejection reason is not persisted on the Supplier aggregate - <c>Reject(reason)</c>
    /// changes state and the reason reaches only the audit row. Resolving it back out of the audit
    /// log by action name would be fragile in a way that fails silently, which is worse than the
    /// exposure it removes. So the reason travels as an argument and lands in the job store.</para>
    ///
    /// <para>It is a reviewer's words about a supplier, not a credential and not PII in the sense
    /// MSP-87 was about, but it is not nothing either. Persisting it on the aggregate would make it
    /// resolvable like the others; that is a schema change and belongs to its own ticket rather than
    /// to a security fix that needs to ship.</para>
    /// </summary>
    public Task SendApplicationRejectedEmailAsync(Guid userId, string reason, CancellationToken ct) =>
        SendToUserAsync(userId, _ => (
            "Your supplier application was not approved",
            $"<p>Your supplier application was rejected for the following reason:</p><p>{reason}</p>" +
            "<p>You may correct the issue and register again.</p>"), ct);

    /// <summary>Takes the annotation id: its Reason is persisted, so it is resolved here rather than
    /// carried through the job store.</summary>
    public async Task SendInfoRequestedEmailAsync(Guid userId, Guid annotationId, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var reason = await db.SupplierReviewAnnotations
            .Where(a => a.Id == annotationId).Select(a => a.Reason).FirstOrDefaultAsync(ct);
        if (reason is null) return;

        await emailSender.SendAsync(email, "Action needed on your supplier application",
            $"<p>The reviewer has requested more information:</p><p>{reason}</p>" +
            "<p>Please log in to address the flagged items and resubmit.</p>", ct);
    }

    /// <summary>Goes to a reviewer, not to the supplier. The reference code is resolved from the
    /// supplier rather than passed - it is the public identifier, but a job argument that can be
    /// derived is a job argument that can drift.</summary>
    public async Task SendApplicationResubmittedEmailAsync(Guid reviewerUserId, Guid supplierId, CancellationToken ct)
    {
        var email = await EmailForAsync(reviewerUserId, ct);
        if (email is null) return;

        var referenceCode = await db.Suppliers
            .Where(s => s.Id == supplierId).Select(s => s.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        await emailSender.SendAsync(email, $"Supplier application {referenceCode} resubmitted",
            $"<p>Supplier application {referenceCode} has addressed the flagged items and been " +
            "resubmitted for review.</p>", ct);
    }

    // ---- document lifecycle ---------------------------------------------------------------
    //
    // These take a document id and resolve the filename from it. MSP-87 found original filenames
    // exposed by the same dashboard route as the addresses; a filename is weaker than an email but
    // it is still the supplier's data, and here it costs nothing to stop carrying it.

    public Task SendDocumentRejectedEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (name, reason) => ("A document on your supplier profile was rejected",
                $"<p>Your document \"{name}\" was rejected for the following reason:</p><p>{reason}</p>" +
                "<p>Please correct the issue and re-upload it.</p>"), ct);

    public Task SendDocumentExpiringEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (name, _) => ("A document on your supplier profile is expiring soon",
                $"<p>Your document \"{name}\" will expire soon. Please renew and re-upload it.</p>"), ct);

    public Task SendDocumentExpiredEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (name, _) => ("A document on your supplier profile has expired",
                $"<p>Your document \"{name}\" has expired and your profile is now flagged incomplete. " +
                "Please re-upload it.</p>"), ct);

    private async Task SendDocumentEmailAsync(
        Guid userId, Guid documentId,
        Func<string, string?, (string Subject, string Body)> compose, CancellationToken ct)
    {
        var email = await EmailForAsync(userId, ct);
        if (email is null) return;

        var document = await db.SupplierDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.OriginalFileName, d.RejectReason })
            .FirstOrDefaultAsync(ct);
        if (document is null) return;

        var (subject, body) = compose(document.OriginalFileName, document.RejectReason);
        await emailSender.SendAsync(email, subject, body, ct);
    }
}
