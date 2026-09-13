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

/// <summary>FEAT-09.1/FR-PRP-001, BUSINESS-PROCESSES.md §4.1: Active + Invitation are checked here
/// (cross-aggregate, same split as InviteSupplierHandler's own Active check); uniqueness is
/// idempotent - a second start returns the existing Draft rather than erroring, per FEAT-09.1's own
/// AC, with the DB unique(rfq_id, supplier_id) index as the real race-safe guarantee underneath.</summary>
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
