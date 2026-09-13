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

/// <summary>
/// A-8/BRULE-067: the recusal declaration window.
///
/// <para><b>This read does NOT open scoring</b>, which is the whole reason it is a separate endpoint:
/// GET my-evaluation transitions the evaluation to InProgress as a side effect, so an evaluator who
/// loaded the workspace first would have passed the window before ever seeing a name.</para>
/// </summary>
public sealed class GetConflictDeclarationHandler(AppDbContext db, IScopeContext scope) : IGetConflictDeclarationHandler
{
    public async Task<ConflictDeclarationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByAssignmentAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;
        var (rfq, evaluation) = loaded.Value;

        var assignment = evaluation.Assignments.FirstOrDefault(a => a.EvaluatorUserId == scope.UserId && a.IsActive);
        if (assignment is null) return null;

        // The window is closed once declared. Returning the names anyway would make the anonymity during
        // scoring decorative: an evaluator could re-read this endpoint mid-scoring and look up whose bid
        // they were marking.
        if (assignment.ConflictDeclaredAt is not null)
        {
            return new ConflictDeclarationDto(false, []);
        }

        // Joined and filtered in SQL, then projected and ordered IN MEMORY. Projecting into a record and
        // then ordering by one of its properties is the shape that answered 500 on the reference-data
        // list in batch 9 - it either translates or does not depending on the provider version. A
        // committee's bid list is a handful of rows, so the round trip is the same either way.
        var rows = await db.Proposals.AsNoTracking()
            .Where(p => p.RfqId == rfq.Id && ProposalStates.InEvaluation.Contains(p.State))
            .Join(db.Suppliers.AsNoTracking(), p => p.SupplierId, sup => sup.Id,
                (p, sup) => new { p.ReferenceCode, sup.DisplayNameAr, sup.DisplayNameEn })
            .ToListAsync(ct);

        var bidders = rows
            .OrderBy(r => r.ReferenceCode, StringComparer.Ordinal)
            .Select(r => new DeclarationBidderDto(r.ReferenceCode, r.DisplayNameAr, r.DisplayNameEn))
            .ToList();

        return new ConflictDeclarationDto(true, bidders);
    }
}
