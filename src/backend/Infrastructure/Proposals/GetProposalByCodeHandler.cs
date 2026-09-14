// Reading one bid by its own public code, as its own supplier.
//
// The ownership test lives inside the shared loader's query, so a code belonging to another company is a miss
// rather than a refusal that confirms the code exists.

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

public sealed class GetProposalByCodeHandler(AppDbContext db, IScopeContext scope) : IGetProposalByCodeHandler
{
    public async Task<ProposalResult> HandleAsync(string proposalReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        return loaded is null
            ? new ProposalResult.NotFoundOrNotInvited()
            : new ProposalResult.Success(ProposalDtoMapper.ToDto(loaded.Value.Proposal, loaded.Value.Rfq.ReferenceCode));
    }
}
