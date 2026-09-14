// Turning a catalogue entry into its read model, including its free-form attributes.
//
// The attributes column is a plain serialised dictionary. Nothing and an empty dictionary both round-trip to
// nothing rather than to an empty object, so a caller reads "no attributes set" and "attributes explicitly
// cleared" the same way either way.

namespace MotsSupplierPortal.Infrastructure.Suppliers;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class OfferingDtoMapper
{
    public static OfferingDto ToDto(Offering o) => new(
        o.Id, o.NameAr, o.NameEn, o.Description, o.CategoryCode, o.UnitOfMeasureCode, o.PriceAmount, o.CurrencyCode, o.IsActive,
        DeserializeAttributes(o.AttributesJson),
        o.RowVersion);

    public static string? SerializeAttributes(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null || attributes.Count == 0 ? null : JsonSerializer.Serialize(attributes);

    public static IReadOnlyDictionary<string, string>? DeserializeAttributes(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}
