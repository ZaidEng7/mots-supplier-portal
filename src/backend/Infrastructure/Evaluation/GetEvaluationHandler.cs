// Reading one evaluation as buyer staff.
//
// It resolves two sets of names that the read model cannot look up for itself.
//
// The evaluators' names, because the assignments table was printing raw identifiers and the recuse button
// beside each row therefore named nobody. A manager deciding whether to recuse an evaluator was reading a
// database identifier.
//
// And the bids' public codes, because the consolidated results table was rendering an internal identifier on
// the screen where a manager decides who wins.

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

public sealed class GetEvaluationHandler(AppDbContext db, IScopeContext scope) : IGetEvaluationHandler
{
    public async Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;

        var assignedIds = loaded.Value.Evaluation.Assignments.Select(a => a.EvaluatorUserId).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => assignedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var resultIds = loaded.Value.Evaluation.Results.Select(r => r.ProposalId).ToList();
        var proposalCodes = await db.Proposals.AsNoTracking()
            .Where(p => resultIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ReferenceCode, ct);

        return EvaluationDtoMapper.ToDto(loaded.Value.Evaluation, loaded.Value.Rfq, proposalCodes, names);
    }
}
