// Archiving a scoring template so nobody can bind it to a new tender.
//
// Archiving does not touch the tenders that already bound it. Each of those holds its own frozen copy of the
// criteria, which is what makes the archive safe.

namespace MotsSupplierPortal.Infrastructure.Evaluation;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Evaluation;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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
