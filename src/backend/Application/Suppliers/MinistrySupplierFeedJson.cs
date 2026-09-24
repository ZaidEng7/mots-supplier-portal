// Feed 1 as JSON, which is the representation the ministry's requirements ask for. The CSV stays, because that
// is what has been handed over by file so far, and both are written from the one projection beside this file.
//
// THE NAMES ARE SPELLED OUT RATHER THAN INHERITED. This product serialises with the web defaults, which lower
// the first letter of every property: SupplierID would go out as supplierID and TaxID as taxID. Their loader
// reads by name, so every property says its own name, and they are declared in the CSV's column order so the
// two can be read side by side.
//
// AN ABSENT VALUE IS null AND STILL PRESENT. A supplier with no address has no Latitude, and the property is
// null rather than missing: a loader reading a fixed set of names should find all of them on every row, and
// four of these are Must fields whose emptiness is a fact the ministry needs to see rather than a hole in the
// document.
//
// THE FLAGS STAY 0 AND 1 rather than becoming true and false. Their sheet specifies 0/1, and JSON being able to
// express a boolean is not a reason to answer a different question than the one they asked.

namespace MotsSupplierPortal.Application.Suppliers;

using System.Globalization;
using System.Text.Json.Serialization;

public sealed record MinistrySupplierFeedJsonRow(
    [property: JsonPropertyName("SupplierID")] string SupplierId,
    [property: JsonPropertyName("SupplierName")] string? SupplierName,
    [property: JsonPropertyName("SupplierNameAr")] string? SupplierNameAr,
    [property: JsonPropertyName("SupplierCode")] string? SupplierCode,
    [property: JsonPropertyName("SupplierGroup")] string? SupplierGroup,
    [property: JsonPropertyName("ApprovalStatus")] string ApprovalStatus,
    [property: JsonPropertyName("Disabled")] int Disabled,
    [property: JsonPropertyName("DefaultCurrency")] string? DefaultCurrency,
    [property: JsonPropertyName("RegistrationType")] string? RegistrationType,
    [property: JsonPropertyName("CommercialRegisterNo")] string? CommercialRegisterNo,
    [property: JsonPropertyName("TaxID")] string? TaxId,
    [property: JsonPropertyName("Country")] string? Country,
    [property: JsonPropertyName("AddressLine")] string? AddressLine,
    [property: JsonPropertyName("City")] string? City,
    [property: JsonPropertyName("Governorate")] string? Governorate,
    [property: JsonPropertyName("Latitude")] double? Latitude,
    [property: JsonPropertyName("Longitude")] double? Longitude,
    [property: JsonPropertyName("Phone")] string? Phone,
    [property: JsonPropertyName("Email")] string? Email,
    [property: JsonPropertyName("PortalUser")] int? PortalUser,
    [property: JsonPropertyName("CreatedOn")] string CreatedOn,
    [property: JsonPropertyName("LastModified")] string LastModified);

public static class MinistrySupplierFeedJson
{
    public static MinistrySupplierFeedJsonRow Row(MinistrySupplierFeedRecord record) =>
        Row(MinistrySupplierFeedProjection.Of(record));

    public static MinistrySupplierFeedJsonRow Row(MinistrySupplierFeedRow row) => new(
        row.SupplierId,
        row.SupplierName,
        row.SupplierNameAr,
        row.SupplierCode,
        row.SupplierGroup,
        row.ApprovalStatus,
        row.Disabled ? 1 : 0,
        row.DefaultCurrency,
        row.RegistrationType,
        row.CommercialRegisterNo,
        row.TaxId,
        row.Country,
        row.AddressLine,
        row.City,
        row.Governorate,
        row.Latitude,
        row.Longitude,
        row.Phone,
        row.Email,
        row.PortalUser is null ? null : row.PortalUser.Value ? 1 : 0,
        row.CreatedOn.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.LastModified.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
}
