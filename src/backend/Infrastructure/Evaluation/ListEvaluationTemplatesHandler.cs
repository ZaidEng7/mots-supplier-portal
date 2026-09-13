using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Evaluation;

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
