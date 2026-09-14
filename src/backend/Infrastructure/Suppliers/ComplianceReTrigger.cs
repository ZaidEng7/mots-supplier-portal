// Recording that editing a compliance-critical field sent an approved supplier back for review.
//
// Shared by the three mutation paths that can do it: legal information, bank account and categories. It
// runs after the domain mutation, writes its own distinct audit action, and enqueues an outbox event, in the
// same commit as the change that caused it.
//
//
// IT IS TOLD WHETHER A RETRIGGER HAPPENED RATHER THAN GUESSING
//
// It used to take the state from before the mutation and re-derive the answer by comparing it with the
// state after: approved before, under review now.
//
// That gave the right answer only because one method in the whole domain model could produce that
// particular transition. Correct by there being no other way to be wrong yet, rather than by anything
// enforcing it. Any future transition landing on the same two states for an unrelated reason would have
// been misattributed as a compliance retrigger, and any future compliance path that bypassed that method
// would have had its retrigger silently missed.
//
// Once the dispatcher that actually delivers outbox rows existed, "correct" stopped meaning "nothing reads
// this, so a wrong attribution is harmless". A misattributed or missing event is now a wrong or absent
// notification downstream rather than an inert row.
//
// So it takes the domain's own answer: the exact value the guard already computes and used to discard.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

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
