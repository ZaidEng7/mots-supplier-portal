// A reviewer asks a supplier for more information, naming the fields and documents at fault.
//
// The request is held in a local so the email job can reference it by identifier. The reason text is
// persisted here, so the job resolves it rather than carrying it in the job store.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class RequestInfoHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger, IBackgroundJobClient backgroundJobs) : IRequestInfoHandler
{
    public async Task<ReviewDecisionResult> HandleAsync(RequestInfoCommand command, CancellationToken ct)
    {
        var supplier = await db.Suppliers.IncludeProfile().FirstOrDefaultAsync(s => s.ReferenceCode == command.ReferenceCode, ct);
        if (supplier is null) return new ReviewDecisionResult.NotFound();

        try { supplier.RequestInfo(); }
        catch (DomainException ex) { return new ReviewDecisionResult.InvalidState(ex.Message); }

        var flaggedTypeIds = await db.DocumentTypes
            .Where(t => command.FlaggedDocumentTypeCodes.Contains(t.Code))
            .Select(t => t.Id)
            .ToListAsync(ct);

        var annotation = new SupplierReviewAnnotation
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            RequestedByUserId = scope.UserId ?? Guid.Empty,
            RequestedAt = DateTimeOffset.UtcNow,
            Reason = command.Reason,
            FlaggedProfileFields = [.. command.FlaggedProfileFields],
            FlaggedDocumentTypeIds = [.. flaggedTypeIds],
        };

        db.SupplierReviewAnnotations.Add(annotation);

        await auditLogger.LogAsync("Supplier", supplier.Id, "application_info_requested", scope.UserId, toState: supplier.OnboardingState.ToString(), reason: command.Reason, referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        var userId = await ReviewerNotify.GetPrimaryUserIdAsync(db, supplier.Id, ct);
        if (userId is not null) backgroundJobs.Enqueue<EmailJobs>(job => job.SendInfoRequestedEmailAsync(userId.Value, annotation.Id, CancellationToken.None));

        return new ReviewDecisionResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
