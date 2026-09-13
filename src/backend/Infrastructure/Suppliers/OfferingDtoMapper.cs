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
