// A bidder asks a question about a tender.
//
// Only an actually invited supplier can post, and only inside the clarification window. Both are enforced
// through the same shared loader every other supplier-facing action uses rather than a reimplementation.
//
// The organization's procurement officers are emailed. The buyer side also sees the question on its next
// fetch, which is the same in-app convention invitations use: there is no per-tender buyer contact to email
// against.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SupplierPostClarificationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : ISupplierPostClarificationHandler
{
    public async Task<SupplierRfqResult> HandleAsync(PostClarificationQuestionCommand command, CancellationToken ct)
    {
        var loaded = await SupplierRfqLoader.LoadInvitedAsync(db, scope, command.ReferenceCode, ct);
        if (loaded is null) return new SupplierRfqResult.NotFoundOrNotInvited();
        var (rfq, invitation) = loaded.Value;

        Clarification clarification;
        try
        {
            clarification = rfq.PostClarificationQuestion(scope.SupplierId!.Value, command.Question);
        }
        catch (DomainException ex)
        {
            return new SupplierRfqResult.InvalidState(ex.Message);
        }

        db.Clarifications.Add(clarification);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_clarification_posted", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"clarificationId\":\"{clarification.Id}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var officerUserIds = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            join u in db.Users on ur.UserId equals u.Id
            where r.Name == Domain.Identity.Roles.ProcurementOfficer && u.OrganizationId == rfq.OrganizationId
            select u.Id)
            .Distinct()
            .ToListAsync(ct);
        foreach (var userId in officerUserIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendClarificationPostedEmailAsync(userId, rfq.Id, clarification.Id, CancellationToken.None));
        }

        return new SupplierRfqResult.Success(RfqDtoMapper.ToSupplierDto(rfq, invitation, scope.SupplierId!.Value));
    }
}
