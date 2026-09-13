using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-10.1/FR-CLR-001/FR-CLR-005: only an actually-invited supplier can post, and only
/// within the clarification window - both enforced through the same SupplierRfqLoader every other
/// supplier-facing action uses, not a reimplementation. FEAT-10.6: audited and notified (the buyer
/// side sees the new question on next dashboard fetch, same "in-app" convention as invitations -
/// no per-RFQ buyer-contact concept exists to email against).</summary>
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
