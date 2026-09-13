// Creating a tender, inside the caller's own organization.
//
// The creator owns what they created.
//
// There is no illegal-transition answer here. Creation has no current state to report an allowed-next set
// against, so every refusal from the factory is a bad request about the request.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

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

public sealed class CreateRfqHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICreateRfqHandler
{
    public async Task<RfqMutationResult> HandleAsync(CreateRfqCommand command, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "RFQ", ct);

        Rfq rfq;
        try
        {
            rfq = Rfq.Create(
                referenceCode, scope.OrganizationId.Value, command.TitleAr, command.TitleEn,
                command.DescriptionAr, command.DescriptionEn, command.CurrencyCode,
                command.PublishAt, command.SubmissionOpensAt, command.SubmissionClosesAt,
                command.ClarificationDeadlineAt, command.EvaluationTargetDate,
                scope.UserId);
        }
        catch (DomainException ex)
        {
            return new RfqMutationResult.InvalidState(ex.Message);
        }

        db.Rfqs.Add(rfq);
        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_created", scope.UserId, referenceCode: rfq.ReferenceCode, toState: nameof(RfqState.Draft), ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
