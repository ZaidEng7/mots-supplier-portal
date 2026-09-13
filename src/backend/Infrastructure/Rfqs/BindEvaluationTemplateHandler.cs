using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Notifications;
using MotsSupplierPortal.Domain.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Rfqs;
using MotsSupplierPortal.Domain.Evaluation;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Domain.Rfqs;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Registrations;

namespace MotsSupplierPortal.Infrastructure.Rfqs;

/// <summary>FEAT-07.3/FR-RFQ-004: binds a version-snapshotted EvaluationTemplateRef. Loads the
/// live EvaluationTemplate (must be Active), serializes its current criteria as the frozen
/// snapshot, marks it IsReferenced (immutable from here on unless forked - EvaluationTemplate.cs's
/// own doc comment), and binds the RFQ to that exact Id+Version. Both aggregates are saved in the
/// same SaveChangesAsync call - a pragmatic single-unit-of-work exception to "one aggregate per
/// transaction" (DOMAIN-MODEL.md §8), justified the same way AuditLogger already is: marking a
/// template referenced is not a domain event that needs eventual consistency, it is the direct,
/// synchronous consequence of the bind command the caller just issued.</summary>
public sealed class BindEvaluationTemplateHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IBindEvaluationTemplateHandler
{
    public async Task<RfqMutationResult> HandleAsync(BindEvaluationTemplateCommand command, CancellationToken ct)
    {
        var rfq = await RfqLoader.LoadScopedAsync(db, scope, command.ReferenceCode, ct);
        if (rfq is null) return new RfqMutationResult.NotFoundOrOutOfScope();

        var template = await db.EvaluationTemplates.Include(t => t.Criteria)
            .FirstOrDefaultAsync(t => t.Id == command.EvaluationTemplateId, ct);
        if (template is null)
        {
            return new RfqMutationResult.InvalidEvaluationTemplate("Evaluation template not found.");
        }
        if (template.Status != EvaluationTemplateStatus.Active)
        {
            return new RfqMutationResult.InvalidEvaluationTemplate("Only an Active evaluation template can be bound to an RFQ.");
        }

        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(template.Criteria.Select(c => new
        {
            c.Id,
            c.NameAr,
            c.NameEn,
            Dimension = c.Dimension.ToString(),
            c.Weight,
            c.MaxScore,
            c.Threshold,
            ScoringType = c.ScoringType.ToString(),
            // T-021: carried into the snapshot so the rule is the one the RFQ bound, not the one the
            // template happens to hold now.
            c.RequiresJustification,
            // SCR-501, and the same argument one field further: the guidance an evaluator scores against
            // must be the instruction in force when this tender bound the template. It was absent from
            // both this snapshot and the evaluation's own, so the text existed on the template and reached
            // nobody - which is why the evaluator's screen rendered name, weight and max and nothing else.
            c.GuidanceAr,
            c.GuidanceEn,
        }));

        try
        {
            rfq.BindEvaluationTemplate(template.Id, template.Version, snapshotJson);
            template.MarkReferenced();
        }
        catch (DomainException ex)
        {
            return RfqTransitions.Refusal(rfq, ex);
        }

        await auditLogger.LogAsync("Rfq", rfq.Id, "rfq_evaluation_template_bound", scope.UserId,
            referenceCode: rfq.ReferenceCode, toState: $"{template.Id}/v{template.Version}", ct: ct);
        await db.SaveChangesAsync(ct);
        return new RfqMutationResult.Success(await RfqDtoMapper.ToDtoAsync(db, rfq, ct));
    }
}
