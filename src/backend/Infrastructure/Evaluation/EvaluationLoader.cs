using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using System.Globalization;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Application.Proposals;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using EvaluationAggregate = MotsSupplierPortal.Domain.Evaluation.Evaluation;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

internal static class EvaluationLoader
{
    public static IQueryable<EvaluationAggregate> IncludeAll(this DbSet<EvaluationAggregate> set) =>
        set.Include(e => e.Criteria).Include(e => e.Assignments).Include(e => e.Scores).Include(e => e.Results).AsSplitQuery();

    /// <summary>Buyer-side: scoped to the caller's own Organization via the bound Rfq, same shape
    /// as RfqLoader.LoadScopedAsync.</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByOrgAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        return evaluation is null ? null : (rfq, evaluation);
    }

    /// <summary>Evaluator-side: scoped to "this caller holds an active assignment on this
    /// evaluation" - deliberately NOT to OrganizationId (an evaluator need not belong to the
    /// procuring organization).</summary>
    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByAssignmentAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.UserId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        if (evaluation is null) return null;
        if (!evaluation.Assignments.Any(a => a.EvaluatorUserId == scope.UserId && a.IsActive)) return null;
        return (rfq, evaluation);
    }

    public static Task<List<Guid>> SubmittedProposalIdsAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        db.Proposals.Where(p => p.RfqId == rfqId && ProposalStates.InEvaluation.Contains(p.State)).Select(p => p.Id).ToListAsync(ct);

    /// <summary>
    /// T-067: every bid under evaluation on this RFQ, projected to its TECHNICAL envelope.
    ///
    /// <para><b>The seal is the projection, not a filter applied afterwards.</b> This is a
    /// <c>Select</c> in SQL that never names <c>ProposalItem</c>, <c>CurrencyCode</c>,
    /// <c>PaymentTerms</c> or any other commercial column - so no pricing row is loaded into memory
    /// for an evaluator to leak by accident, and adding one would mean editing this projection
    /// rather than forgetting a filter. Same reasoning as ProposalDtoMapper's note on why the
    /// two-envelope seal was "not a filter applied to a shared read".</para>
    ///
    /// <para>Documents are Technical-envelope only (D-7). They are NOT filtered on scan state, and
    /// that is a correction to this method's first version: proposal documents are scanned on first
    /// ACCESS (D-10), so nothing scans them until a download happens - filtering the list to Clean
    /// made it permanently empty, in production as well as in the test that caught it. PendingScan
    /// means "not yet examined", not "suspect"; listing a file is not serving it, and the download
    /// route still scans and still refuses. See DECISIONS-TAKEN.md D-20.</para>
    /// </summary>
    public static Task<List<EvaluatorBid>> EvaluatorBidsAsync(AppDbContext db, Guid rfqId, CancellationToken ct) =>
        db.Proposals
            .Where(p => p.RfqId == rfqId && ProposalStates.InEvaluation.Contains(p.State))
            .OrderBy(p => p.ReferenceCode)
            .Select(p => new EvaluatorBid(
                p.Id,
                p.ReferenceCode,
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.ReferenceCode).First(),
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.DisplayNameAr).First(),
                db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.DisplayNameEn).First(),
                p.NarrativeAr,
                p.NarrativeEn,
                p.RequirementAnswers
                    .Select(a => new RequirementAnswerDto(a.Id, a.RequirementId, a.AnswerAr, a.AnswerEn))
                    .ToList(),
                p.Documents
                    .Where(d => d.Envelope == ProposalDocumentEnvelope.Technical
                                && d.ScanState != AttachmentScanState.ScanRejected)
                    .OrderBy(d => d.UploadedAt)
                    .Select(d => new EvaluatorProposalDocumentDto(
                        d.Id, d.OriginalFileName, d.ContentType, d.Caption, d.UploadedAt))
                    .ToList()))
            .ToListAsync(ct);
}
