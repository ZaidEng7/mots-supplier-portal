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

public sealed class SetCommercialTermsHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISetCommercialTermsHandler
{
    public async Task<ProposalResult> HandleAsync(SetCommercialTermsCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        // T-072. The same rule as the merge-patch path. This handler's ROUTE was retired when
        // §12.5 moved the edit onto PATCH - it is registered in DI and mapped by nothing - so the
        // check is unreachable today. Added anyway: the cost is four lines, and a handler that is
        // re-mapped later without it is the shape this whole entry is about.
        var (knownIncoterm, resolvedIncoterm) = await IncotermRule.ResolveAsync(db, command.IncotermCode, ct);
        if (!knownIncoterm)
        {
            return new ProposalResult.InvalidState(await IncotermRule.RefusalDetailAsync(db, command.IncotermCode, ct));
        }

        try
        {
            proposal!.SetCommercialTerms(command.CurrencyCode, command.PaymentTerms, resolvedIncoterm,
                command.DeliveryTermsAr, command.DeliveryTermsEn, command.Warranty, command.ValidityStart, command.ValidityEnd);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_terms_updated", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
