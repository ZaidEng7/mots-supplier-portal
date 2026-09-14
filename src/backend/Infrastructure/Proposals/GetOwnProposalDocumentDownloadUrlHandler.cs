// A supplier opens a file on their own bid.
//
// No envelope question arises here. The two-envelope rule keeps a buyer from seeing pricing before the gate
// opens; it has nothing to say about a bidder reading their own bid, and a rule applied where it does not
// belong is how a supplier ends up locked out of their own upload.
//
// The document is resolved THROUGH the bid, which is itself resolved with the ownership test inside the
// query, so a document identifier belonging to somebody else's bid is a miss rather than a leak.

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class GetOwnProposalDocumentDownloadUrlHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetOwnProposalDocumentDownloadUrlHandler
{
    public async Task<ProposalDocumentDownloadResult> HandleAsync(
        string proposalReferenceCode, Guid documentId, CancellationToken ct)
    {
        var loaded = await ProposalLoader.LoadByProposalCodeAsync(db, scope, proposalReferenceCode, ct);
        if (loaded is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();
        var (_, proposal) = loaded.Value;

        var document = proposal.Documents.FirstOrDefault(d => d.Id == documentId);
        if (document is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        return await ProposalDocumentDownload.MintAsync(
            db, scope, fileStorage, auditLogger, attachmentScanner, proposal, document, ct);
    }
}
