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

public sealed class CloseRfqSubmissionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICloseRfqSubmissionHandler
{
    public async Task<RfqMutationResult> HandleAsync(CloseRfqSubmissionCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.CloseSubmissionWindow(command.Reason, isEarlyClose: true);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex, RfqState.SubmissionClosed);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_submission_closed", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(RfqState.SubmissionOpen), toState: nameof(RfqState.SubmissionClosed), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
