using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

public sealed class CreateEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ICreateEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateMutationResult> HandleAsync(CreateEvaluationTemplateCommand command, CancellationToken ct)
    {
        EvaluationTemplate template;
        try
        {
            template = EvaluationTemplate.Create(command.NameAr, command.NameEn);
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        db.EvaluationTemplates.Add(template);
        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "evaluation_template_created", scope.UserId, toState: template.NameEn, ct: ct);
        await db.SaveChangesAsync(ct);

        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }
}
