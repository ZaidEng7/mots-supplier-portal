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

public sealed class StartProposalHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IStartProposalHandler
{
    public async Task<ProposalResult> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, existing) = loaded.Value;

        // Start stays idempotent for a LIVE proposal - a second click returns the same draft rather
        // than making another. A WITHDRAWN one is different: BUSINESS-PROCESSES.md §4.1 permits
        // "re-submission allowed while window open (new draft)", so a withdrawal is not a bar to
        // starting again, it is the absence of a current proposal.
        //
        // Before this, a withdrawn proposal was returned here as though it were the supplier's
        // current one - and every edit path then refused it, because it is not a Draft. A supplier
        // who withdrew to correct a price could never bid on that RFQ again, silently and
        // permanently. The withdrawal window guard is unchanged: this only applies while the RFQ is
        // SubmissionOpen, because SupplierRfqLoader.LoadInvitedAsync is what got us here.
        if (existing is not null && existing.State != ProposalState.Withdrawn)
        {
            return new ProposalResult.Success(ProposalDtoMapper.ToDto(existing, rfq.ReferenceCode));
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == scope.SupplierId!.Value, ct);
        if (supplier is null || supplier.LifecycleState != SupplierLifecycleState.Active)
        {
            return new ProposalResult.NotFoundOrNotInvited();
        }

        var referenceCode = await ReferenceCodeGenerator.NextCodeAsync(db, "PRP", ct);
        var proposal = Proposal.Create(referenceCode, rfq.Id, scope.SupplierId!.Value);
        db.Proposals.Add(proposal);
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_started", scope.UserId, referenceCode: proposal.ReferenceCode, toState: nameof(ProposalState.Draft), ct: ct);
        await db.SaveChangesAsync(ct);

        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
