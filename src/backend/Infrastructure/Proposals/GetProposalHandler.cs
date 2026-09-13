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

public sealed class GetProposalHandler(AppDbContext db, IScopeContext scope) : IGetProposalHandler
{
    public async Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadAsync(db, scope, rfqReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal!, rfq.ReferenceCode));
    }
}
