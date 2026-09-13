using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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
