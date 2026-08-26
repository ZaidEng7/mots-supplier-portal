using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

/// <summary>STORY-03.1.1 AC2/AC3: the domain refuses the ProfileInProgress -> Submitted
/// transition server-side when required fields are missing - the UI cannot bypass this.</summary>
public sealed class SubmitApplicationHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ISubmitApplicationHandler
{
    public async Task<SubmitApplicationResult> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null)
        {
            return new SubmitApplicationResult.NotFoundOrOutOfScope();
        }

        var supplier = await db.Suppliers
            .Include(s => s.Representatives)
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
            "Supplier", supplier.Id, "application_submitted", Guid.NewGuid(), scope.UserId,
            toState: supplier.OnboardingState.ToString(), referenceCode: supplier.ReferenceCode, ct: ct);
        await db.SaveChangesAsync(ct);

        return new SubmitApplicationResult.Success(SupplierDtoMapper.ToDto(supplier));
    }
}
