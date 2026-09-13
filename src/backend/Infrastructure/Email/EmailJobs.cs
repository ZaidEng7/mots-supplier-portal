// Every email the product sends, as background jobs.
//
// Each one is queued rather than sent inline, so delivery is durable and retried and never blocks the request
// that triggered it.
//
//
// EVERY ARGUMENT IS AN IDENTIFIER, AND THAT IS A SECURITY PROPERTY RATHER THAN A STYLE
//
// The job framework persists arguments as plain text in its own tables, and those rows outlive the emails they
// produced, because succeeded jobs are retained on a timer.
//
// A security review found a supplier administrator reading fifteen other suppliers' email addresses out of that
// store, together with live verification and password-reset links. The token in one of them was a working
// credential.
//
// That review restricted the dashboard, which is a rule about who may look. This is the other half: there is
// nothing there to read. The rule protects one surface; the absence protects every surface, including backups,
// replicas, a support query, log aggregation, and whatever gets built over the job data later. The written
// privacy rule reasons about personal data in logs, and this store is far closer to a log than to the audit
// table.
//
//
// TOKENS ARE ISSUED HERE, NOT PASSED IN
//
// A token baked into a job argument is a credential at rest for the whole retention window.
//
// Issuing at send time also shortens the window in which the token is useful, and a retry minting a fresh one is
// correct rather than a side effect: the previous one expires on its own.
//
// The same reasoning covers the recipient's language, the tender's public code, a document's filename and a
// reviewer's annotation. The job resolves its own facts about the user rather than trusting an argument that
// could go stale between enqueueing and sending, and a filename is still the supplier's data even though it is
// weaker than an address.
//
//
// ONE ARGUMENT BREAKS THAT RULE, AND IT IS NAMED
//
// An information request's reason travels as an argument and lands in the job store, because the supplier's
// record changes state and the reason reaches only the audit row. Resolving it back out of the audit log by
// action name would be fragile in a way that fails silently, which is worse than the exposure it removes.
//
// It is a reviewer's words about a supplier: not a credential, and not personal data in the sense that review
// was about, but not nothing either. Persisting it on the record would make it resolvable like the others, and
// that is a schema change belonging to its own piece of work rather than to a security fix that needs to ship.
//
//
// A RECIPIENT WHO HAS GONE IS SILENCE, NOT A FAILURE
//
// A user deleted between enqueueing and sending is not an error worth retrying. The framework would retry to
// exhaustion and then surface a failed job needing a human to dismiss it.
//
// The send is best-effort by construction, and the durable record of what happened is the audit trail rather
// than the job.
//
//
// EVERY SEND GOES THROUGH THE COPY SOURCE
//
// So an administrator's rewording is never inert. The shipped copy is passed as a lambda rather than looked up
// by key: this method has the typed arguments in hand, so it cannot render the fallback with the wrong ones, and
// the fallback is only evaluated when no override exists, which is the normal case.
//
// A reject reason is only present on a rejection, and an override of the expiry wording that referenced it would
// have been refused when it was written, because those templates do not declare that token.

namespace MotsSupplierPortal.Infrastructure.Email;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class EmailJobs(
    IEmailSender emailSender,
    AppDbContext db,
    ISecurityTokenService securityTokenService,
    IConfiguration configuration,
    MotsSupplierPortal.Application.Admin.IEmailCopySource copySource)
{
    private Task<(string Subject, string Body)> ComposeAsync(
        string key,
        string? locale,
        Dictionary<string, string> tokens,
        Func<(string Subject, string Body)> shipped,
        CancellationToken ct) =>
        copySource.ComposeAsync(key, locale, tokens, shipped, ct);

    private async Task<(string Email, string? Language)?> RecipientAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.Language })
            .FirstOrDefaultAsync(ct);
        return recipient is null || recipient.Email is null ? null : (recipient.Email, recipient.Language);
    }

    private async Task SendToUserAsync(
        Guid userId,
        string templateKey,
        Func<string?, (string Subject, string Body)> compose,
        CancellationToken ct,
        Dictionary<string, string>? tokens = null)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var (subject, body) = await ComposeAsync(
            templateKey, recipient.Value.Language, tokens ?? [], () => compose(recipient.Value.Language), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    private string PublicUrl => configuration["App:PublicUrl"]
        ?? throw new InvalidOperationException("App:PublicUrl is not configured.");

    public async Task SendVerificationEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.EmailVerification, TimeSpan.FromHours(24), ct);
        var verifyUrl = $"{PublicUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.Verification, recipient.Value.Language,
            new() { ["verifyUrl"] = verifyUrl }, () => EmailTemplates.Verification(recipient.Value.Language, verifyUrl), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendPasswordResetEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.PasswordReset, TimeSpan.FromMinutes(30), ct);
        var resetUrl = $"{PublicUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.PasswordReset, recipient.Value.Language,
            new() { ["resetUrl"] = resetUrl }, () => EmailTemplates.PasswordReset(recipient.Value.Language, resetUrl), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendSupplierUserInviteEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.SupplierUserInvite, TimeSpan.FromDays(7), ct);
        var acceptUrl = $"{PublicUrl}/accept-invite?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.SupplierUserInvite, recipient.Value.Language,
            new() { ["acceptUrl"] = acceptUrl }, () => EmailTemplates.SupplierUserInvite(recipient.Value.Language, acceptUrl), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendStaffInviteEmailAsync(Guid userId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rawToken = await securityTokenService.IssueAsync(
            userId, SecurityTokenPurpose.StaffInvite, TimeSpan.FromDays(7), ct);
        var acceptUrl = $"{PublicUrl}/accept-staff-invite?token={Uri.EscapeDataString(rawToken)}";

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.StaffInvite, recipient.Value.Language,
            new() { ["acceptUrl"] = acceptUrl }, () => EmailTemplates.StaffInvite(recipient.Value.Language, acceptUrl), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public Task SendAlreadyRegisteredNoticeEmailAsync(Guid userId, CancellationToken ct) =>
        SendToUserAsync(userId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.AlreadyRegisteredNotice,
            locale => EmailTemplates.AlreadyRegisteredNotice(locale, PublicUrl), ct,
            new() { ["publicUrl"] = PublicUrl });

    public Task SendApplicationApprovedEmailAsync(Guid userId, CancellationToken ct) =>
        SendToUserAsync(userId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ApplicationApproved, EmailTemplates.ApplicationApproved, ct);

    public Task SendApplicationRejectedEmailAsync(Guid userId, string reason, CancellationToken ct) =>
        SendToUserAsync(userId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ApplicationRejected,
            locale => EmailTemplates.ApplicationRejected(locale, reason), ct,
            new() { ["reason"] = reason });

    public async Task SendInfoRequestedEmailAsync(Guid userId, Guid annotationId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var reason = await db.SupplierReviewAnnotations
            .Where(a => a.Id == annotationId).Select(a => a.Reason).FirstOrDefaultAsync(ct);
        if (reason is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.InfoRequested, recipient.Value.Language,
            new() { ["reason"] = reason }, () => EmailTemplates.InfoRequested(recipient.Value.Language, reason), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendApplicationResubmittedEmailAsync(Guid reviewerUserId, Guid supplierId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(reviewerUserId, ct);
        if (recipient is null) return;

        var referenceCode = await db.Suppliers
            .Where(s => s.Id == supplierId).Select(s => s.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ApplicationResubmitted, recipient.Value.Language,
            new() { ["referenceCode"] = referenceCode }, () => EmailTemplates.ApplicationResubmitted(recipient.Value.Language, referenceCode), ct);
        await emailSender.SendAsync(reviewerUserId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendRfqInvitationEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rfq = await db.Rfqs.Where(r => r.Id == rfqId)
            .Select(r => new { r.ReferenceCode, r.TitleAr, r.TitleEn }).FirstOrDefaultAsync(ct);
        if (rfq is null) return;

        var title = recipient.Value.Language == "en" ? rfq.TitleEn : rfq.TitleAr;
        var deepLink = $"{PublicUrl}/rfqs/{Uri.EscapeDataString(rfq.ReferenceCode)}";

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.RfqInvitation, recipient.Value.Language,
            new() { ["referenceCode"] = rfq.ReferenceCode, ["rfqTitle"] = title, ["deepLink"] = deepLink }, () => EmailTemplates.RfqInvitation(recipient.Value.Language, rfq.ReferenceCode, title, deepLink), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendClarificationAnsweredEmailAsync(Guid userId, Guid rfqId, Guid clarificationId, CancellationToken ct)
    {
        _ = clarificationId; // key only, not resolved into the email body - see class doc comment
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var referenceCode = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ClarificationAnswered, recipient.Value.Language,
            new() { ["referenceCode"] = referenceCode }, () => EmailTemplates.ClarificationAnswered(recipient.Value.Language, referenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendClarificationPublishedEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var referenceCode = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ClarificationPublished, recipient.Value.Language,
            new() { ["referenceCode"] = referenceCode }, () => EmailTemplates.ClarificationPublished(recipient.Value.Language, referenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendClarificationPostedEmailAsync(Guid userId, Guid rfqId, Guid clarificationId, CancellationToken ct)
    {
        _ = clarificationId;
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var referenceCode = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (referenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ClarificationPosted, recipient.Value.Language,
            new() { ["referenceCode"] = referenceCode }, () => EmailTemplates.ClarificationPosted(recipient.Value.Language, referenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendRfqAddendumEmailAsync(Guid userId, Guid rfqId, Guid addendumId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var rfq = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfq is null) return;
        var addendum = await db.Addenda.Where(a => a.Id == addendumId)
            .Select(a => new { a.TitleAr, a.TitleEn }).FirstOrDefaultAsync(ct);
        if (addendum is null) return;

        var title = recipient.Value.Language == "en" ? addendum.TitleEn : addendum.TitleAr;
        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.RfqAddendum, recipient.Value.Language,
            new() { ["referenceCode"] = rfq, ["addendumTitle"] = title }, () => EmailTemplates.RfqAddendum(recipient.Value.Language, rfq, title), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendRfqPublishedEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;
        var rfq = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfq is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.RfqPublished, recipient.Value.Language,
            new() { ["referenceCode"] = rfq }, () => EmailTemplates.RfqPublished(recipient.Value.Language, rfq), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendRfqCancelledEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;
        var rfq = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfq is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.RfqCancelled, recipient.Value.Language,
            new() { ["referenceCode"] = rfq }, () => EmailTemplates.RfqCancelled(recipient.Value.Language, rfq), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendProposalSubmittedEmailAsync(Guid userId, Guid proposalId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var proposal = await db.Proposals.Where(p => p.Id == proposalId)
            .Select(p => new { p.ReferenceCode, p.RfqId }).FirstOrDefaultAsync(ct);
        if (proposal is null) return;
        var rfqReferenceCode = await db.Rfqs.Where(r => r.Id == proposal.RfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfqReferenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.ProposalSubmitted, recipient.Value.Language,
            new() { ["proposalReferenceCode"] = proposal.ReferenceCode, ["rfqReferenceCode"] = rfqReferenceCode }, () => EmailTemplates.ProposalSubmitted(recipient.Value.Language, proposal.ReferenceCode, rfqReferenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendEvaluatorAssignedEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;
        var rfq = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfq is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.EvaluatorAssigned, recipient.Value.Language,
            new() { ["referenceCode"] = rfq }, () => EmailTemplates.EvaluatorAssigned(recipient.Value.Language, rfq), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendAwardIssuedEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;
        var rfqReferenceCode = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfqReferenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.AwardIssued, recipient.Value.Language,
            new() { ["rfqReferenceCode"] = rfqReferenceCode }, () => EmailTemplates.AwardIssued(recipient.Value.Language, rfqReferenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public async Task SendAwardRegretEmailAsync(Guid userId, Guid rfqId, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;
        var rfqReferenceCode = await db.Rfqs.Where(r => r.Id == rfqId).Select(r => r.ReferenceCode).FirstOrDefaultAsync(ct);
        if (rfqReferenceCode is null) return;

        var (subject, body) = await ComposeAsync(MotsSupplierPortal.Application.Admin.EmailTemplateKeys.AwardRegret, recipient.Value.Language,
            new() { ["rfqReferenceCode"] = rfqReferenceCode }, () => EmailTemplates.AwardRegret(recipient.Value.Language, rfqReferenceCode), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }

    public Task SendDocumentRejectedEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.DocumentRejected,
            (locale, name, reason) => EmailTemplates.DocumentRejected(locale, name, reason), ct);

    public Task SendDocumentExpiringEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.DocumentExpiring,
            (locale, name, _) => EmailTemplates.DocumentExpiring(locale, name), ct);

    public Task SendDocumentExpiredEmailAsync(Guid userId, Guid documentId, CancellationToken ct) =>
        SendDocumentEmailAsync(userId, documentId, MotsSupplierPortal.Application.Admin.EmailTemplateKeys.DocumentExpired,
            (locale, name, _) => EmailTemplates.DocumentExpired(locale, name), ct);

    private async Task SendDocumentEmailAsync(
        Guid userId, Guid documentId, string templateKey,
        Func<string?, string, string?, (string Subject, string Body)> compose, CancellationToken ct)
    {
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return;

        var document = await db.SupplierDocuments
            .Where(d => d.Id == documentId)
            .Select(d => new { d.OriginalFileName, d.RejectReason })
            .FirstOrDefaultAsync(ct);
        if (document is null) return;

        var tokens = new Dictionary<string, string> { ["fileName"] = document.OriginalFileName };
        if (document.RejectReason is { } reason) tokens["reason"] = reason;

        var (subject, body) = await ComposeAsync(templateKey, recipient.Value.Language, tokens,
            () => compose(recipient.Value.Language, document.OriginalFileName, document.RejectReason), ct);
        await emailSender.SendAsync(userId, recipient.Value.Email, subject, body, ct);
    }
}
