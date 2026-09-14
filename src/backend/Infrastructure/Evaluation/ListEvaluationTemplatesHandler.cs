// The catalogue of scoring templates.
//
// Portal-wide, with no scoping beyond the permission gate. Templates are shared across the whole procurement
// organisation rather than held per buying body, which matches the written model: the template carries no
// integration and no per-tenant split.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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
