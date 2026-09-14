// The shared loads every evaluation handler starts from, and the evaluator's sealed view of the bids.
//
// The buyer's load scopes to the caller's own organization through the tender, which is the same shape the
// tender loader has.
//
// The evaluator's load scopes instead to "this caller holds an active assignment on this evaluation", and
// deliberately not to an organization, because an evaluator need not belong to the procuring organization.
//
//
// THE TWO-ENVELOPE SEAL IS THE PROJECTION, NOT A FILTER APPLIED AFTERWARDS
//
// The evaluator's view of the bids is selected in the database and never names a priced line, a currency, a
// payment term or any other commercial column.
//
// So no pricing row is loaded into memory for an evaluator to leak by accident, and adding one would mean
// editing this projection rather than forgetting a filter. The bid read model's own header makes the same
// point.
//
//
// DOCUMENTS ARE TECHNICAL ONLY, AND ARE NOT FILTERED ON SCAN STATE
//
// That second half is a correction to this method's first version. Bid documents are scanned on first ACCESS,
// so nothing scans them until a download happens, and filtering the list to scanned-clean files made it
// permanently empty, in production as well as in the test that caught it.
//
// Unscanned means "not yet examined" rather than "suspect". Listing a file is not serving it, and the download
// route still scans and still refuses.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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

internal static class EvaluationLoader
{
    public static IQueryable<EvaluationAggregate> IncludeAll(this DbSet<EvaluationAggregate> set) =>
        set.Include(e => e.Criteria).Include(e => e.Assignments).Include(e => e.Scores).Include(e => e.Results).AsSplitQuery();

    public static async Task<(Rfq Rfq, EvaluationAggregate Evaluation)?> LoadScopedByOrgAsync(AppDbContext db, IScopeContext scope, string rfqReferenceCode, CancellationToken ct)
    {
        if (scope.OrganizationId is null) return null;
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.ReferenceCode == rfqReferenceCode && r.OrganizationId == scope.OrganizationId, ct);
        if (rfq is null) return null;
        var evaluation = await db.Evaluations.IncludeAll().FirstOrDefaultAsync(e => e.RfqId == rfq.Id, ct);
        return evaluation is null ? null : (rfq, evaluation);
    }

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
