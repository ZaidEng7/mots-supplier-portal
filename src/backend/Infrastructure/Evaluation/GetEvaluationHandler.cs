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

public sealed class GetEvaluationHandler(AppDbContext db, IScopeContext scope) : IGetEvaluationHandler
{
    public async Task<EvaluationDto?> HandleAsync(string rfqReferenceCode, CancellationToken ct)
    {
        var loaded = await EvaluationLoader.LoadScopedByOrgAsync(db, scope, rfqReferenceCode, ct);
        if (loaded is null) return null;

        // Evaluator NAMES, on the read the screen actually renders. The assignments table was printing user
        // GUIDs, and the recuse button beside each row therefore named nobody - a manager deciding whether to
        // recuse an evaluator was reading 01a07461-fa48-7721-abe2-018baaa84d11.
        var assignedIds = loaded.Value.Evaluation.Assignments.Select(a => a.EvaluatorUserId).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => assignedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // And the proposals' §3 reference codes. The consolidated results table was rendering the internal
        // GUID on the screen where a manager decides who wins.
        var resultIds = loaded.Value.Evaluation.Results.Select(r => r.ProposalId).ToList();
        var proposalCodes = await db.Proposals.AsNoTracking()
            .Where(p => resultIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ReferenceCode, ct);

        return EvaluationDtoMapper.ToDto(loaded.Value.Evaluation, loaded.Value.Rfq, proposalCodes, names);
    }
}
