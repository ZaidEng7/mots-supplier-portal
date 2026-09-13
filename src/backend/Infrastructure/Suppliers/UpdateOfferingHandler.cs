// Editing one of a supplier's own catalogue entries.
//
// An entry belonging to a different company reads as not-found rather than as forbidden. The caller must not
// learn that the identifier exists at all.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class UpdateOfferingHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUpdateOfferingHandler
{
    public async Task<OfferingMutationResult> HandleAsync(UpdateOfferingCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var offering = await db.Offerings.FirstOrDefaultAsync(o => o.Id == command.OfferingId && o.SupplierId == scope.SupplierId, ct);
        if (offering is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var validation = await CreateOfferingHandler.ValidateReferencesAsync(db, command.CategoryCode, command.UnitOfMeasureCode, command.CurrencyCode, ct);
        if (validation is not null) return validation;

        var newAttributesJson = OfferingDtoMapper.SerializeAttributes(command.Attributes);
        var changes = AuditChangeBuilder.Build(
            ("nameEn", offering.NameEn, command.NameEn),
            ("categoryCode", offering.CategoryCode, command.CategoryCode),
            ("unitOfMeasureCode", offering.UnitOfMeasureCode, command.UnitOfMeasureCode),
            ("priceAmount", offering.PriceAmount, command.PriceAmount),
            ("currencyCode", offering.CurrencyCode, command.CurrencyCode),
            ("attributes", offering.AttributesJson, newAttributesJson));

        offering.NameAr = command.NameAr;
        offering.NameEn = command.NameEn;
        offering.Description = command.Description;
        offering.CategoryCode = command.CategoryCode;
        offering.UnitOfMeasureCode = command.UnitOfMeasureCode;
        offering.PriceAmount = command.PriceAmount;
        offering.CurrencyCode = command.CurrencyCode;
        offering.AttributesJson = newAttributesJson;

        await auditLogger.LogAsync("Offering", offering.Id, "offering_updated", scope.UserId, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OfferingMutationResult.Success(OfferingDtoMapper.ToDto(offering));
    }
}
