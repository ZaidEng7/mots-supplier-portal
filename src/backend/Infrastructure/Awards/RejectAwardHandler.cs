// A manager rejects a recommended award, with a reason.
//
// The same segregation rule applies as to approval: the person rejecting may not be the person who recommended.
//
// The notification goes to the tender's owner. The reason stays out of the payload and out of the words, and the
// officer reads it on the award screen.

namespace MotsSupplierPortal.Infrastructure.Awards;

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
