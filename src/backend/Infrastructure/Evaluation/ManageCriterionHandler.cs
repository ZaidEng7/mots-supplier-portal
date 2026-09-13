using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

public sealed class ManageCriterionHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IManageCriterionHandler
{
    private async Task<EvaluationTemplate?> LoadAsync(Guid templateId, CancellationToken ct) =>
        await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == templateId, ct);

    public async Task<EvaluationTemplateMutationResult> AddAsync(AddCriterionCommand command, CancellationToken ct)
    {
        var template = await LoadAsync(command.TemplateId, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        Criterion criterion;
        try
        {
            criterion = template.AddCriterion(
                command.NameAr, command.NameEn, command.Dimension, command.Weight, command.MaxScore,
                command.Threshold, command.ScoringType, command.GuidanceAr, command.GuidanceEn,
                command.RequiresJustification);
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        db.Criteria.Add(criterion);
        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "criterion_added", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }

    public async Task<EvaluationTemplateMutationResult> UpdateAsync(UpdateCriterionCommand command, CancellationToken ct)
    {
        var template = await LoadAsync(command.TemplateId, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        try
        {
            template.UpdateCriterion(
                command.CriterionId, command.NameAr, command.NameEn, command.Dimension, command.Weight,
                command.MaxScore, command.Threshold, command.ScoringType, command.GuidanceAr, command.GuidanceEn,
                command.RequiresJustification);
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "criterion_updated", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }

    public async Task<EvaluationTemplateMutationResult> RemoveAsync(RemoveCriterionCommand command, CancellationToken ct)
    {
        var template = await LoadAsync(command.TemplateId, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        try
        {
            template.RemoveCriterion(command.CriterionId);
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "criterion_removed", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }
}
