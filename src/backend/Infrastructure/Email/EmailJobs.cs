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
///
/// <para><b>MSP-69.</b> Every composed subject/body now goes through EmailTemplates, keyed on the
/// recipient's AppUser.Language (resolved here, not passed in - same reasoning as the token: the
/// job resolves its own facts about the user rather than trusting an argument that could go stale
/// between enqueue and send). All 11 templates render both ar and en; missing/unrecognized locale
/// falls back to Arabic, matching AppUser.Language's own default.</para>
/// </summary>
public sealed class EmailJobs(
    IEmailSender emailSender,
    AppDbContext db,
    ISecurityTokenService securityTokenService,
    IConfiguration configuration)
{
    private async Task<(string Email, string? Language)?> RecipientAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.Language })
            .FirstOrDefaultAsync(ct);
        return recipient is null || recipient.Email is null ? null : (recipient.Email, recipient.Language);
    }

    /// <summary>
    /// Resolves the recipient and sends, or does nothing if the user has gone.
    ///
    /// Silence is deliberate. A user deleted between enqueue and send is not an error worth
    /// retrying - Hangfire would retry it to exhaustion and then surface a failed job that needs a
    /// human to dismiss. The send is best-effort by construction; the durable record of what
    /// happened is the audit row the handler already wrote.
    /// </summary>
    private async Task SendToUserAsync(Guid userId, Func<string?, (string Subject, string Body)> compose, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var (subject, body) = compose(recipient.Value.Language);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
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
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), ct);
        var verifyUrl = $"{PublicUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = EmailTemplates.Verification(recipient.Value.Language, verifyUrl);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendPasswordResetEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.PasswordReset, TimeSpan.FromMinutes(30), ct);
        var resetUrl = $"{PublicUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = EmailTemplates.PasswordReset(recipient.Value.Language, resetUrl);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendSupplierUserInviteEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.SupplierUserInvite, TimeSpan.FromDays(7), ct);
        var acceptUrl = $"{PublicUrl}/accept-invite?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = EmailTemplates.SupplierUserInvite(recipient.Value.Language, acceptUrl);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    /// <summary>Task #28: same shape as SendSupplierUserInviteEmailAsync, distinct accept-invite
    /// path (/accept-staff-invite, not /accept-invite) since staff and supplier-user invites are
    /// accepted by different handlers/pages even though the token mechanism is identical.</summary>
    public async Task SendStaffInviteEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromDays(7), ct);
        var acceptUrl = $"{PublicUrl}/accept-staff-invite?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = EmailTemplates.StaffInvite(recipient.Value.Language, acceptUrl);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    /// <summary>MSP-73/enumeration fix: sent to an ALREADY-registered account when someone submits
    /// a new registration using its email (or its supplier's registration number). No token - a
    /// plain link to /login, per the explicit decision to keep this a reminder, not a new
    /// passwordless-auth mechanism. Helps a legitimate user who forgot they'd already registered,
    /// while the API response given to whoever submitted the duplicate stays identical to a real
    /// success (see RegistrationEndpoints.cs) - this email is the ONLY signal that goes anywhere,
    /// and it goes only to the account's own inbox, never back to the submitter.</summary>
    public Task SendAlreadyRegisteredNoticeEmailAsync(Guid userId, CancellationToken ct) =>
        SendToUserAsync(userId, locale => EmailTemplates.AlreadyRegisteredNotice(locale, PublicUrl), ct);

    // ---- application lifecycle ------------------------------------------------------------

    public Task SendApplicationApprovedEmailAsync(Guid userId, CancellationToken ct) =>
        SendToUserAsync(userId, EmailTemplates.ApplicationApproved, ct);

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
        SendToUserAsync(userId, locale => EmailTemplates.ApplicationRejected(locale, reason), ct);

    /// <summary>Takes the annotation id: its Reason is persisted, so it is resolved here rather than
    /// carried through the job store.</summary>
    public async Task SendInfoRequestedEmailAsync(Guid userId, Guid annotationId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var reason = await db.SupplierReviewAnnotations
            .Where(a => a.Id == annotationId).Select(a => a.Reason).FirstOrDefaultAsync(ct);
        if (reason is null) return;

        var (subject, body) = EmailTemplates.InfoRequested(recipient.Value.Language, reason);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    /// <summary>Goes to a reviewer, not to the supplier. The reference code is resolved from the
    /// supplier rather than passed - it is the public identifier, but a job argument that can be
    /// derived is a job argument that can drift.</summary>
    public async Task SendApplicationResubmittedEmailAsync(Guid reviewerUserId, Guid supplierId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(reviewerUserId, ct);
        if (recipient is null) return;

        var referenceCode = await db.Suppliers
            .Where(s => s.Id == supplierId).Select(s => s.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        var (subject, body) = EmailTemplates.ApplicationResubmitted(recipient.Value.Language, referenceCode);
        await emailSender.SendAsync(reviewerUserId, recipient.Value.Email, subject, body, ct);
    }

    // ---- document lifecycle ---------------------------------------------------------------
    //
    // These take a document id and resolve the filename from it. MSP-87 found original filenames
    // exposed by the same dashboard route as the addresses; a filename is weaker than an email but
    // it is still the supplier's data, and here it costs nothing to stop carrying it.

    public Task SendDocumentRejectedEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (locale, name, reason) => EmailTemplates.DocumentRejected(locale, name, reason), ct);

    public Task SendDocumentExpiringEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (locale, name, _) => EmailTemplates.DocumentExpiring(locale, name), ct);

    public Task SendDocumentExpiredEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId,
            (locale, name, _) => EmailTemplates.DocumentExpired(locale, name), ct);

    private async Task SendDocumentEmailAsync(
        Guid userId, Guid documentId,
        Func<string?, string, string?, (string Subject, string Body)> compose, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var document = await db.SupplierDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.OriginalFileName, d.RejectReason })
            .FirstOrDefaultAsync(ct);
        if (document is null) return;

        var (subject, body) = compose(recipient.Value.Language, document.OriginalFileName, document.RejectReason);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }
}
