using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-10.4/FR-CLR-004/FR-RFQ-012: the first real use of "locked after Published except
/// addenda" (Rfq.IssueAddendum's own doc comment). Notifies every invited supplier.</summary>
public sealed class IssueAddendumHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs)
    : IIssueAddendumHandler
{
    public async Task<RfqMutationResult> HandleAsync(IssueAddendumCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();
        if (scope.UserId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        Addendum addendum;
        try
        {
            addendum = rfq.IssueAddendum(command.TitleAr, command.TitleEn, command.DescriptionAr, command.DescriptionEn, scope.UserId.Value);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        db.Addenda.Add(addendum);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_addendum_issued", scope.UserId,
            referenceCode: rfq.ReferenceCode, changes: $"{{\"addendumId\":\"{addendum.Id}\"}}", ct: ct);
        await db.SaveChangesAsync(ct);

        var invitedSupplierIds = rfq.Invitations.Select(i => i.SupplierId).ToList();
        var userIds = invitedSupplierIds.Count == 0
            ? []
            : await db.Users.Where(u => u.SupplierId != null && invitedSupplierIds.Contains(u.SupplierId.Value)).Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            backgroundJobs.Enqueue<EmailJobs>(job => job.SendRfqAddendumEmailAsync(userId, rfq.Id, addendum.Id, CancellationToken.None));
        }

        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
