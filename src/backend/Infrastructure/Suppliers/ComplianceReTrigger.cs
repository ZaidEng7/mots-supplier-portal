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
/// ApproveApplicationHandler already uses.</summary>
internal static class ComplianceReTrigger
{
    public static async Task LogIfReTriggeredAsync(AppDbContext db, IAuditLogger auditLogger, Supplier supplier, SupplierOnboardingState stateBefore, string fieldChanged, Guid? actorUserId, CancellationToken ct)
    {
        if (stateBefore != SupplierOnboardingState.Approved || supplier.OnboardingState != SupplierOnboardingState.UnderReview)
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
