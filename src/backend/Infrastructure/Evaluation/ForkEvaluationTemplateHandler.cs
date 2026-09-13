// Forking a scoring template into a new version.
//
// This is how a template that a live tender has bound gets changed: the bound version stays immutable and the
// fork is the one that is edited.
//
// The copied criteria are each tracked explicitly, because they are new rows whose identifiers are assigned in
// the copy rather than by the database.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ForkEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IForkEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct)
    {
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        var forked = template.Fork();
        db.EvaluationTemplates.Add(forked);
        foreach (var criterion in forked.Criteria) db.Criteria.Add(criterion);

        await auditLogger.LogAsync("EvaluationTemplate", forked.Id, "evaluation_template_forked", scope.UserId,
            fromState: $"{template.Id}/v{template.Version}", toState: $"v{forked.Version}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(forked));
    }
}
