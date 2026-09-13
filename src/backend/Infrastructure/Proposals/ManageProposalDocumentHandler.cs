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

/// <summary>FEAT-09.3/FR-PRP-004: stored via IFileStorage directly, same convention as
/// RfqAttachment (no AV-scan quarantine flow here either - OQ-014 already tags AV scanning
/// generally as [REQUIRES BUSINESS CONFIRMATION], same deliberate scope decision as RFQ
/// attachments).</summary>
public sealed class ManageProposalDocumentHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageProposalDocumentHandler
{
    public async Task<ProposalResult> AddAsync(AddProposalDocumentCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        ProposalDocument document;
        try
        {
            document = proposal!.AddDocument(
                command.StorageKey, command.OriginalFileName, command.ContentType, command.Caption, command.Envelope);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        db.ProposalDocuments.Add(document);
        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_document_added", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }

    public async Task<ProposalResult> RemoveAsync(RemoveProposalDocumentCommand command, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, command.ProposalReferenceCode, ct);
        if (loaded?.Proposal is null) return new ProposalResult.NotFoundOrNotInvited();
        var (rfq, proposal) = loaded.Value;

        try
        {
            proposal!.RemoveDocument(command.DocumentId);
        }
        catch (DomainException ex)
        {
            return new ProposalResult.InvalidState(ex.Message, proposal!.State);
        }

        await auditLogger.LogAsync("Proposal", proposal.Id, "proposal_document_removed", scope.UserId, referenceCode: proposal.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);
        return new ProposalResult.Success(ProposalDtoMapper.ToDto(proposal, rfq.ReferenceCode));
    }
}
