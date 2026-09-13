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

public sealed class UpdateRfqBasicsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUpdateRfqBasicsHandler
{
    public async Task<RfqMutationResult> HandleAsync(UpdateRfqBasicsCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        try
        {
            rfq.UpdateBasics(
                command.TitleAr, command.TitleEn, command.DescriptionAr, command.DescriptionEn, command.CurrencyCode,
                command.PublishAt, command.SubmissionOpensAt, command.SubmissionClosesAt,
                command.ClarificationDeadlineAt, command.EvaluationTargetDate);
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_updated", scope.UserId, referenceCode: rfq.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
