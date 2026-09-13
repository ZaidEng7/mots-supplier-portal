// Reading one scoring template with its criteria.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class GetEvaluationTemplateHandler(AppDbContext db) : IGetEvaluationTemplateHandler
{
    public async Task<EvaluationTemplateDto?> HandleAsync(Guid id, CancellationToken ct)
    {
        var template = await db.EvaluationTemplates.Include(t => t.Criteria).FirstOrDefaultAsync(t => t.Id == id, ct);
        return template is null ? null : EvaluationTemplateDtoMapper.ToDto(template);
    }
}
