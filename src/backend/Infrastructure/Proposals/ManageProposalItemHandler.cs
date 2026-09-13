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

/// <summary>FEAT-09.1/FR-PRP-002: the financial envelope. Nothing here differs structurally from any
/// other Draft-only edit handler - the envelope separation lives in the schema/DTO layer (see
/// ProposalDtoMapper's own doc comment), not in extra guards on writes.</summary>
public sealed class ManageProposalItemHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageProposalItemHandler
{
    public async Task<ProposalResult> SetAsync(SetItemPricingCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.SetItemPricing(command.RfqItemId, command.Quantity, command.UnitPrice, command.Discount, command.LeadTimeDays, command.NotesAr, command.NotesEn);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        db.ProposalItems.Add(proposal.Items.First(i => i.RfqItemId == command.RfqItemId));
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_item_priced", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }

    public async Task<ProposalResult> RemoveAsync(RemoveItemPricingCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.RemoveItemPricing(command.RfqItemId);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_item_removed", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
