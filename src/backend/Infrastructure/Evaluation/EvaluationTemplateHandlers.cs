using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

internal static class EvaluationTemplateDtoMapper
{
    public static EvaluationTemplateDto ToDto(EvaluationTemplate t) => new(
        t.Id, t.FamilyId, t.Version, t.NameAr, t.NameEn, t.Status, t.IsReferenced,
        [.. t.Criteria.OrderBy(c => c.SortOrder).Select(c => new CriterionDto(
            c.Id, c.NameAr, c.NameEn, c.Dimension, c.Weight, c.MaxScore, c.Threshold, c.ScoringType,
            c.GuidanceAr, c.GuidanceEn, c.SortOrder, c.RequiresJustification))],
        t.RowVersion);
}

/// <summary>FEAT-11.1/FR-ADM-005, pulled forward for EPIC-07. Portal-only, no row-scoping beyond
/// the permission gate (evaluation.template.manage) - templates are shared across the whole
/// procurement org, not per-Organization, matching EvaluationTemplate's aggregate catalogue entry
/// (DOMAIN-MODEL.md §3: no ERP sync, no per-tenant split described).</summary>
public sealed class ListEvaluationTemplatesHandler(AppDbContext db) : IListEvaluationTemplatesHandler
{
    public async Task<IReadOnlyList<EvaluationTemplateDto>> HandleAsync(CancellationToken ct)
    {
        var templates = await db.EvaluationTemplates
            .Include(t => t.Criteria)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return [.. templates.Select(EvaluationTemplateDtoMapper.ToDto)];
    }
}

public sealed class GetEvaluationTemplateHandler(AppDbContext db) : IGetEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateDto?> HandleAsync(Guid id, CancellationToken ct)
    {
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == id, ct);
        return template is null ? null : EvaluationTemplateDtoMapper.ToDto(template);
    }
}

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

public sealed class ArchiveEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IArchiveEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateMutationResult> HandleAsync(Guid id, CancellationToken ct)
    {
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return new EvaluationTemplateMutationResult.NotFound();

        try
        {
            template.Archive();
        }
        catch (DomainException ex)
        {
            return new EvaluationTemplateMutationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync("EvaluationTemplate", template.Id, "evaluation_template_archived", scope.UserId,
            toState: nameof(EvaluationTemplateStatus.Archived), ct: ct);
        await db.SaveChangesAsync(ct);
        return new EvaluationTemplateMutationResult.Success(EvaluationTemplateDtoMapper.ToDto(template));
    }
}

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
