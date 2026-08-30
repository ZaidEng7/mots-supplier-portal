using System.Text.Json;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>FEAT-04.9/STORY-04.9.1: shared by UpdateLegalInfoHandler, ManageBankAccountHandler,
/// and ManageCategoryLinkHandler - the three compliance-critical mutation paths. Called after the
/// domain mutation; if it flipped Approved -> UnderReview (EnsureEditableForComplianceField), logs
/// a distinct audit action and writes an Outbox event, same atomic-with-the-mutation pattern
/// ApproveApplicationHandler already uses.
///
/// <para><b>Task #18/MSP-82.</b> Used to take <c>stateBefore</c> and re-derive whether a retrigger
/// happened by comparing it against <c>supplier.OnboardingState</c> after the mutation
/// (<c>stateBefore == Approved &amp;&amp; OnboardingState == UnderReview</c>). That heuristic gave
/// the right answer only because <c>EnsureEditableForComplianceField</c> happened to be the sole
/// code path in the whole domain model that could ever produce an Approved -> UnderReview
/// transition - correct by there being no other way to be wrong yet, not by anything that actually
/// enforces it. Any future transition landing on the same two states for an unrelated reason would
/// have been silently misattributed as a compliance retrigger (or a real retrigger silently missed,
/// if a future compliance-critical path bypassed EnsureEditableForComplianceField).
///
/// Now that Task #16 built a real dispatcher that actually delivers what this writes to the
/// Outbox, "correct" stopped meaning "nothing currently reads this so a wrong attribution is
/// harmless" - a misattributed or missed event is now a real downstream consequence (a wrong or
/// absent "SupplierProfileChanged" notification), not an inert row. The fix: take the domain's own
/// authoritative <c>bool</c> - the exact value <c>EnsureEditableForComplianceField</c> already
/// computes and previously discarded - instead of re-deriving an approximation of it from state
/// that has since changed.</para>
/// </summary>
internal static class ComplianceReTrigger
{
    public static async Task LogIfReTriggeredAsync(AppDbContext db, IAuditLogger auditLogger, Supplier supplier, bool reTriggered, string fieldChanged, Guid? actorUserId, CancellationToken ct)
    {
        if (!reTriggered)
        {
            return;
        }

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "compliance_field_changed_review_retriggered", actorUserId,
            fromState: nameof(SupplierOnboardingState.Approved), toState: nameof(SupplierOnboardingState.UnderReview),
            reason: fieldChanged, referenceCode: supplier.ReferenceCode, ct: ct);

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "SupplierProfileChanged",
            PayloadJson = JsonSerializer.Serialize(new
            {
                supplierId = supplier.Id,
                referenceCode = supplier.ReferenceCode,
                fieldChanged,
                changedAt = DateTimeOffset.UtcNow,
            }),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}
