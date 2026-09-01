using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Suppliers;

internal static class OfferingDtoMapper
{
    public static OfferingDto ToDto(Offering o) => new(
        o.Id, o.NameAr, o.NameEn, o.Description, o.CategoryCode, o.UnitOfMeasureCode, o.PriceAmount, o.CurrencyCode, o.IsActive);
}

/// <summary>FEAT-06.1/FR-OFF-001: an offering is only ever listed for the caller's own supplier
/// (IScopeContext.SupplierId, derived from the JWT - never client input) - this is the row-scoping
/// half of FEAT-06.1's acceptance criteria, same pattern as every other supplier-scoped list.</summary>
public sealed class ListOfferingsHandler(AppDbContext db, IScopeContext scope) : IListOfferingsHandler
{
    public async Task<IReadOnlyList<OfferingDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];

        return await db.Offerings
            .Where(o => o.SupplierId == scope.SupplierId)
            .OrderBy(o => o.NameEn)
            .Select(o => OfferingDtoMapper.ToDto(o))
            .ToListAsync(ct);
    }
}

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

public sealed class UpdateOfferingHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IUpdateOfferingHandler
{
    public async Task<OfferingMutationResult> HandleAsync(UpdateOfferingCommand command, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        // Row-scoping: an offering belonging to a DIFFERENT supplier reads as not-found, never as
        // forbidden - the caller must not learn that the id exists at all.
        var offering = await db.Offerings.FirstOrDefaultAsync(o => o.Id == command.OfferingId && o.SupplierId == scope.SupplierId, ct);
        if (offering is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var validation = await CreateOfferingHandler.ValidateReferencesAsync(db, command.CategoryCode, command.UnitOfMeasureCode, command.CurrencyCode, ct);
        if (validation is not null) return validation;

        var changes = AuditChangeBuilder.Build(
            ("nameEn", offering.NameEn, command.NameEn),
            ("categoryCode", offering.CategoryCode, command.CategoryCode),
            ("unitOfMeasureCode", offering.UnitOfMeasureCode, command.UnitOfMeasureCode),
            ("priceAmount", offering.PriceAmount, command.PriceAmount),
            ("currencyCode", offering.CurrencyCode, command.CurrencyCode));

        offering.NameAr = command.NameAr;
        offering.NameEn = command.NameEn;
        offering.Description = command.Description;
        offering.CategoryCode = command.CategoryCode;
        offering.UnitOfMeasureCode = command.UnitOfMeasureCode;
        offering.PriceAmount = command.PriceAmount;
        offering.CurrencyCode = command.CurrencyCode;

        await auditLogger.LogAsync("Offering", offering.Id, "offering_updated", scope.UserId, changes: changes, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OfferingMutationResult.Success(OfferingDtoMapper.ToDto(offering));
    }
}

/// <summary>FEAT-06.1 AC2: deactivation hides the offering from future buyer discovery but keeps
/// the row (and its history) - never a delete.</summary>
public sealed class DeactivateOfferingHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger) : IDeactivateOfferingHandler
{
    public async Task<OfferingMutationResult> HandleAsync(Guid offeringId, CancellationToken ct)
    {
        if (scope.SupplierId is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        var offering = await db.Offerings.FirstOrDefaultAsync(o => o.Id == offeringId && o.SupplierId == scope.SupplierId, ct);
        if (offering is null) return new OfferingMutationResult.NotFoundOrOutOfScope();

        offering.IsActive = false;

        await auditLogger.LogAsync("Offering", offering.Id, "offering_deactivated", scope.UserId, ct: ct);
        await db.SaveChangesAsync(ct);

        return new OfferingMutationResult.Success(OfferingDtoMapper.ToDto(offering));
    }
}
