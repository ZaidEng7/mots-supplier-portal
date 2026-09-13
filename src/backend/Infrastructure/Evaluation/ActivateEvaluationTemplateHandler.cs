using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

public sealed class ActivateEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IActivateEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct)
    {
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        try
        {
            template.Activate();
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "evaluation_template_activated", scope.UserId,
            toState: nameof(EvaluationTemplateStatus.Active), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }
}
