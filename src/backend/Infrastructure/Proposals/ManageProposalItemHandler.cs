// Pricing and unpricing the lines of a bid: the financial envelope.
//
// Nothing here differs structurally from any other draft-only edit. The envelope separation lives in the read
// models and the schema rather than in extra guards on writes; the read model's own header explains that.
//
// The retired per-line routes are superseded by the single partial edit, which is where the duplicate-insert
// hazard around re-pricing an already-tracked line is explained.

namespace MotsSupplierPortal.Infrastructure.Proposals;

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
