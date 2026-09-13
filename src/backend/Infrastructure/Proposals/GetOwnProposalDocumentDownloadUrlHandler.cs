using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Proposals;

/// <summary>
/// T-028's supplier half: a supplier reading a file on their own proposal.
///
/// <para>No envelope question arises here. The two-envelope rule keeps a buyer from seeing pricing
/// before the technical gate opens; it has nothing to say about a bidder reading their own bid, and
/// a rule applied where it does not belong is how a supplier ends up locked out of their own
/// upload.</para>
///
/// <para>The document is resolved THROUGH the proposal, which is itself resolved through
/// <see cref="ProposalLoader.LoadByProposalCodeAsync"/> - so the SupplierId predicate is in the
/// query and a document id belonging to someone else's proposal is a miss, not a leak.</para>
/// </summary>
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
