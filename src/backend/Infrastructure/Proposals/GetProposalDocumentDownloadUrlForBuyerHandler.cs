// Buyer staff open one file from a bid.
//
// The visibility test and the scan-then-mint sequence are both shared, so this route adds nothing of its own
// beyond finding the document.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class GetProposalDocumentDownloadUrlForBuyerHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetProposalDocumentDownloadUrlForBuyerHandler
{
    public async Task<ProposalDocumentDownloadResult> HandleAsync(
        string rfqReferenceCode, Guid proposalId, Guid documentId, CancellationToken ct)
    {
        var proposal = await BuyerVisibleProposal.LoadAsync(db, scope, rfqReferenceCode, proposalId, ct);
        if (proposal is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        var document = proposal.Documents.FirstOrDefault(d => d.Id == documentId);
        if (document is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        return await ProposalDocumentDownload.MintAsync(
            db, scope, fileStorage, auditLogger, attachmentScanner, proposal, document, ct);
    }
}
