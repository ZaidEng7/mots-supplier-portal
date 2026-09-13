using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Awards;
using MotsSupplierPortal.Application.Comparison;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Awards;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Awards;

public sealed class RejectAwardHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IRejectAwardHandler
{
    public async Task<AwardMutationResult> HandleAsync(RejectAwardCommand command, CancellationToken ct)
    {
        var loaded = await AwardLoader.LoadScopedAsync(db, scope, command.RfqReferenceCode, ct);
        if (loaded is null || loaded.Value.Award is null) return new AwardMutationResult.NotFoundOrOutOfScope();
        var (rfq, award) = loaded.Value;

        if (scope.UserId == award.RecommendedByUserId)
        {
            return new AwardMutationResult.SegregationOfDutiesViolation();
        }

        try
        {
            award.Reject(scope.UserId!.Value, command.Reason);
        }
        catch (DomainException ex)
        {
            return new AwardMutationResult.InvalidState(ex.Message);
        }

        // §3.4 "PendingApproval -> Rejected | In-app to officer" - A-7: the RFQ's owner. The
        // rejection REASON stays out of the payload and out of the words (BRULE-091); the officer
        // reads it on the award screen.
        NotificationOutbox.EnqueueMany(db, NotificationTypes.AwardRejected,
            await NotificationRecipients.RfqOwnerAsync(db, rfq, ct),
            $"{NotificationTypes.AwardRejected}:{award.Id}:{award.RecommendationRevision}",
            new Dictionary<string, string?> { ["rfqCode"] = rfq.ReferenceCode, ["awardId"] = award.Id.ToString() });

        await auditLogger.LogAsync("Award", award.Id, "award.rejected", scope.UserId, referenceCode: rfq.ReferenceCode,
            fromState: nameof(AwardState.PendingApproval), toState: nameof(AwardState.Rejected), reason: command.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new AwardMutationResult.Success(AwardDtoMapper.ToDto(award, rfq.ReferenceCode, await AwardWinner.CodeAsync(db, award, ct)));
    }
}
