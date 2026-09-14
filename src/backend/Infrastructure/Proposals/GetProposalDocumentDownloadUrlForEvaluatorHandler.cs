// An evaluator opens one document from a bid they are scoring.
//
// The list an evaluator reads is built elsewhere; this is the route that opens one of its rows.
//
//
// THE TWO GATES ARE THE LIST'S OWN, RESTATED
//
// Restated because a download is reachable by guessing an identifier and a list is not. The caller must hold an
// ACTIVE assignment on this tender's evaluation, and the document must be in the technical envelope.
//
// A commercial document is the same not-found as one that does not exist. An evaluator who learns that a bid
// has three commercial attachments has learned something about a competitor's pricing before consolidation.
//
// The assignment IS the scope, and it is deliberately not also filtered by organization. An assignment is
// granted per evaluation, so an evaluator holding one is in scope by definition, and adding an organization
// test would refuse a legitimately borrowed evaluator and would be a second, divergent answer to "may this
// caller see this evaluation".

namespace MotsSupplierPortal.Infrastructure.Proposals;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class GetProposalDocumentDownloadUrlForEvaluatorHandler(
    AppDbContext db, IScopeContext scope, IFileStorage fileStorage, IAuditLogger auditLogger,
    AttachmentScanner attachmentScanner)
    : IGetProposalDocumentDownloadUrlForEvaluatorHandler
{
    public async Task<ProposalDocumentDownloadResult> HandleAsync(
        string rfqReferenceCode, string proposalCode, Guid documentId, CancellationToken ct)
    {
        if (scope.SupplierId is not null || scope.UserId is null)
        {
            return new ProposalDocumentDownloadResult.NotFoundOrForbidden();
        }

        var rfqId = await db.Rfqs.AsNoTracking()
            .Where(r => r.ReferenceCode == rfqReferenceCode)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (rfqId is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        var assigned = await db.Evaluations.AsNoTracking()
            .Where(e => e.RfqId == rfqId)
            .SelectMany(e => e.Assignments)
            .AnyAsync(a => a.EvaluatorUserId == scope.UserId && a.RecusedAt == null, ct);
        if (!assigned) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        var proposal = await db.Proposals
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(
                p => p.ReferenceCode == proposalCode
                     && p.RfqId == rfqId
                     && ProposalStates.InEvaluation.Contains(p.State), ct);
        if (proposal is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        var document = proposal.Documents.FirstOrDefault(
            d => d.Id == documentId && d.Envelope == ProposalDocumentEnvelope.Technical);
        if (document is null) return new ProposalDocumentDownloadResult.NotFoundOrForbidden();

        return await ProposalDocumentDownload.MintAsync(
            db, scope, fileStorage, auditLogger, attachmentScanner, proposal, document, ct);
    }
}
