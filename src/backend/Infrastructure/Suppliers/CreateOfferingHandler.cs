using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

public sealed class CreateOfferingHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : ICreateOfferingHandler
{
    public async Task<OfferingMutationResult> HandleAsync(CreateOfferingCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var validation = await ValidateReferencesAsync(db, command.CategoryCode, command.UnitOfMeasureCode, command.CurrencyCode, ct);
        if (validation is not null) return validation;

        var offering = new Offering
        {
            Id = Guid.CreateVersion7(),
            SupplierId = scope.SupplierId.Value,
            NameAr = command.NameAr,
            NameEn = command.NameEn,
            Description = command.Description,
            CategoryCode = command.CategoryCode,
            UnitOfMeasureCode = command.UnitOfMeasureCode,
            PriceAmount = command.PriceAmount,
            CurrencyCode = command.CurrencyCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            AttributesJson = OfferingDtoMapper.SerializeAttributes(command.Attributes),
        };
        db.Offerings.Add(offering);

        await auditLogger.LogAsync("Offering", offering.Id, "offering_created", scope.UserId, toState: command.NameEn, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OfferingMutationResult.Success(OfferingDtoMapper.ToDto(offering));
    }

    internal static async Task<OfferingMutationResult?> ValidateReferencesAsync(
        AppDbContext db, string categoryCode, string unitOfMeasureCode, string? currencyCode, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Code == categoryCode && c.IsActive, ct))
        {
            return new OfferingMutationResult.InvalidCategory();
        }
        if (!await db.UnitsOfMeasure.AnyAsync(u => u.Code == unitOfMeasureCode && u.IsActive, ct))
        {
            return new OfferingMutationResult.InvalidUnitOfMeasure();
        }
        if (currencyCode is not null && !await db.Currencies.AnyAsync(c => c.Code == currencyCode && c.IsActive, ct))
        {
            return new OfferingMutationResult.InvalidCurrency();
        }
        return null;
    }
}
