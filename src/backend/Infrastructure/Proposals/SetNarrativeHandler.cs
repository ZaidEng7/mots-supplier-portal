using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Rfqs;

namespace MotsSupplierPortal.Infrastructure.Proposals;

public sealed class SetNarrativeHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISetNarrativeHandler
{
    public async Task<ProposalResult> HandleAsync(SetNarrativeCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.SetNarrative(command.NarrativeAr, command.NarrativeEn);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_narrative_updated", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
