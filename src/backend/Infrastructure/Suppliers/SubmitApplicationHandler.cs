// A supplier submits their registration for review.
//
// The domain refuses the transition on the server when required fields are missing, so the interface cannot
// bypass the gate by hiding the button.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SubmitApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitApplicationHandler
{
    public async Task<SubmitApplicationResult> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new SubmitApplicationResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .IncludeProfile()
            .FirstOrDefaultAsync(s => s.Id == scope.SupplierId, ct);

        if (supplier is null)
        {
            return new SubmitApplicationResult.NotFoundOrOutOfScope();
        }

        var missingProfileFields = supplier.GetMissingProfileFields();
        var missingDocumentTypes = await DocumentCompletenessEvaluator.GetMissingRequiredDocumentTypeCodesAsync(db, supplier.Id, ct);
        var missing = missingProfileFields.Concat(missingDocumentTypes).ToList();
        if (missing.Count > 0)
        {
            return new SubmitApplicationResult.Incomplete(missing);
        }

        try
        {
            supplier.Submit(missingDocumentTypes);
        }
        catch (DomainException ex)
        {
            return new SubmitApplicationResult.InvalidState(ex.Message);
        }

        await auditLogger.LogAsync(
            "Supplier", supplier.Id, "application_submitted", scope.UserId,
            toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new SubmitApplicationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
