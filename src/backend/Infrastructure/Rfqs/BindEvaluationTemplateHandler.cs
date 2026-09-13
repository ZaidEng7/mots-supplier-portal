// Binding a tender to one exact version of a scoring template, and freezing its criteria.
//
// The live template must be active. Its current criteria are serialised as the frozen snapshot, the template
// is marked as referenced, which makes it immutable from then on unless somebody forks it, and the tender is
// bound to that identifier and version.
//
//
// TWO AGGREGATES IN ONE COMMIT
//
// A pragmatic exception to one aggregate per transaction, justified the same way the audit logger's is:
// marking a template referenced is not an event that needs to settle eventually, it is the direct,
// synchronous consequence of the command the caller just issued.
//
//
// WHAT THE SNAPSHOT HAS TO CARRY
//
// Whether a criterion requires a justification is in the snapshot, so the rule is the one the tender bound
// rather than the one the template happens to hold now.
//
// So is the guidance text, for the same argument one field further: the instruction an evaluator scores
// against must be the one in force when this tender bound the template. It was absent from both this
// snapshot and the evaluation's own, so the text existed on the template and reached nobody, which is why
// the evaluator's screen rendered a name, a weight and a maximum and nothing else.

namespace MotsSupplierPortal.Infrastructure.Rfqs;

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
            c.RequiresJustification,
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
