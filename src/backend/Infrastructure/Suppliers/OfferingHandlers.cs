using System.Text.Json;
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
        o.Id, o.NameAr, o.NameEn, o.Description, o.CategoryCode, o.UnitOfMeasureCode, o.PriceAmount, o.CurrencyCode, o.IsActive,
        DeserializeAttributes(o.AttributesJson),
        o.RowVersion);

    /// <summary>FEAT-06.2: the jsonb column is a plain serialized dictionary (see Offering.AttributesJson's
    /// doc comment) - null/empty round-trips to null, never an empty object, so a caller can tell
    /// "no attributes set" apart from "attributes explicitly cleared" the same way either way.</summary>
    public static string? SerializeAttributes(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null || attributes.Count == 0 ? null : JsonSerializer.Serialize(attributes);

    public static IReadOnlyDictionary<string, string>? DeserializeAttributes(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}

/// <summary>FEAT-06.1/FR-OFF-001: an offering is only ever listed for the caller's own supplier
/// (IScopeContext.SupplierId, derived from the JWT - never client input) - this is the row-scoping
/// half of FEAT-06.1's acceptance criteria, same pattern as every other supplier-scoped list.</summary>
public sealed class ListOfferingsHandler(AppDbContext db, IScopeContext scope) : IListOfferingsHandler
{
    public async Task<IReadOnlyList<OfferingDto>> HandleAsync(CancellationToken ct)
    {
        if (scope.SupplierId is null) return [];

        var offerings = await db.Offerings
            .Where(o => o.SupplierId == scope.SupplierId)
            .OrderBy(o => o.NameEn)
            .ToListAsync(ct);

        return offerings.Select(OfferingDtoMapper.ToDto).ToList();
    }
}

/// <summary>
/// One offering, scoped to the caller's own supplier IN THE QUERY - a miss is indistinguishable from
/// an id that never existed (§9.2), same as every other supplier-scoped read here.
/// </summary>
public sealed class GetOfferingHandler(AppDbContext db, IScopeContext scope) : IGetOfferingHandler
{
    public async Task<OfferingDto?> HandleAsync(Guid offeringId, CancellationToken ct)
    {
        if (scope.SupplierId is null) return null;

        var offering = await db.Offerings.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offeringId && o.SupplierId == scope.SupplierId, ct);

        return offering is null ? null : OfferingDtoMapper.ToDto(offering);
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

/// <summary>FEAT-06.3/FR-OFF-004: procurement staff discovering offerings across all suppliers.
/// FEAT-06.4/FR-OFF-005: gated on Supplier.LifecycleState == Active, not Offering.IsActive alone -
/// a supplier suspended after listing an offering must disappear from buyer search even though the
/// Offering row itself is untouched by suspension. No EF navigation exists between Offering and
/// Supplier (both are deliberately separate aggregate roots, see Offering.cs's doc comment), so
/// this is a manual join on the plain SupplierId FK rather than an owned/included navigation.</summary>
public sealed class SearchBuyerOfferingsHandler(AppDbContext db) : ISearchBuyerOfferingsHandler
{
    public async Task<IReadOnlyList<BuyerOfferingSearchResultDto>> HandleAsync(string? categoryCode, string? query, CancellationToken ct)
    {
        var offerings = db.Offerings.Where(o => o.IsActive);
        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            offerings = offerings.Where(o => o.CategoryCode == categoryCode);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            // The caller's text is escaped before it becomes a LIKE PATTERN.
            //
            // Interpolated raw, `%` and `_` in the search box were pattern syntax rather than
            // characters: `?query=%` matched every row and `?query=a_c` matched "abc". Not SQL
            // injection - the value is still a parameter - but the caller's string stopped meaning
            // what it says, which is the same class of surprise.
            //
            // Not a disclosure on THIS endpoint today, because searching with no query already
            // returns every active offering, so the widest a wildcard can reach is what the caller
            // could have had anyway. It becomes one the day this search is row-scoped or paged by
            // relevance, and that is the day nobody would think to look here. Found by EPIC-19's
            // filter-guard check and fixed while it is still cheap.
            var pattern = $"%{LikePattern.Escape(query)}%";
            offerings = offerings.Where(o =>
                EF.Functions.ILike(o.NameEn, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(o.NameAr, pattern, LikePattern.EscapeCharacter));
        }

        var joined = await (
            from o in offerings
            join s in db.Suppliers on o.SupplierId equals s.Id
            where s.LifecycleState == SupplierLifecycleState.Active
            orderby o.NameEn
            select new { Offering = o, Supplier = s }
        ).ToListAsync(ct);

        return joined.Select(x => new BuyerOfferingSearchResultDto(
            x.Offering.Id, x.Supplier.ReferenceCode, x.Supplier.DisplayNameAr, x.Supplier.DisplayNameEn,
            x.Offering.NameAr, x.Offering.NameEn, x.Offering.Description, x.Offering.CategoryCode, x.Offering.UnitOfMeasureCode,
            x.Offering.PriceAmount, x.Offering.CurrencyCode, OfferingDtoMapper.DeserializeAttributes(x.Offering.AttributesJson)))
            .ToList();
    }
}
