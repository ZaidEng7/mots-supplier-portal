// One reading of a supplier for the ministry's feed, in types rather than in text.
//
// WHY THIS EXISTS AT ALL. The feed has two representations now, CSV and JSON, and the first version of the JSON
// one repeated the whole mapping: which name wins when there is no legal name, which address is the one, how a
// lifecycle becomes a flag, what an unknown country does. Two copies of twenty-two decisions, and every future
// change would have to land in both. They would not stay identical - they would stay identical until somebody
// changed one, which is the same thing as a silent divergence between the file the ministry loads by hand and
// the endpoint their dashboard calls at night.
//
// So the decisions live here once, in the types they actually are: a flag is a bool, a coordinate is a double,
// a timestamp is a timestamp. The two representations then differ only in how they WRITE those values, which is
// the only way they are genuinely allowed to differ.
//
// THE FORMATTING DIFFERENCES ARE REAL AND DELIBERATE. CSV has one type, so a flag is written 0 or 1 and a
// timestamp is written without a zone. JSON says what a value is, so the same flag is still 0 or 1 - because
// their sheet specifies 0/1 and being right about the type does not license changing the value they asked for -
// while the timestamp carries its Z and the coordinate is a number rather than a quoted string. A coordinate
// serialised as a string is accepted by most loaders and plotted by none.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Domain.Suppliers;

public sealed record MinistrySupplierFeedRow(
    string SupplierId,
    string? SupplierName,
    string? SupplierNameAr,
    string? SupplierCode,
    string? SupplierGroup,
    string ApprovalStatus,
    bool Disabled,
    string? DefaultCurrency,
    string? RegistrationType,
    string? CommercialRegisterNo,
    string? TaxId,
    string? Country,
    string? AddressLine,
    string? City,
    string? Governorate,
    double? Latitude,
    double? Longitude,
    string? Phone,
    string? Email,
    bool? PortalUser,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastModified);

public static class MinistrySupplierFeedProjection
{
    public static MinistrySupplierFeedRow Of(MinistrySupplierFeedRecord record)
    {
        var s = record.Supplier;
        var legal = s.LegalInfo;

        var address = s.Addresses.FirstOrDefault(a => a.IsPrimary) ?? s.Addresses.FirstOrDefault();
        var representative = s.Representatives.FirstOrDefault(r => r.IsPrimary) ?? s.Representatives.FirstOrDefault();
        var primaryCategory = s.PrimaryCategoryCode;

        return new MinistrySupplierFeedRow(
            s.ReferenceCode,
            legal?.LegalNameEn ?? s.DisplayNameEn,
            legal?.LegalNameAr ?? s.DisplayNameAr,
            null,
            primaryCategory is null
                ? null
                : record.CategoryNameEn.TryGetValue(primaryCategory, out var group) ? group : primaryCategory,
            MinistrySupplierFeedCsv.ApprovalStatus(s.OnboardingState),
            s.LifecycleState is SupplierLifecycleState.Suspended or SupplierLifecycleState.Deactivated,
            s.CurrencyCode,
            legal?.SupplierType.ToString(),
            legal?.RegistrationNumber,
            legal?.TaxId,
            address is null ? null : MinistrySupplierFeedCsv.CountryCode(address.Country),
            address?.Line1,
            address?.City,
            address is null
                ? null
                : record.RegionNameEn.TryGetValue(address.RegionCode, out var region) ? region : address.RegionCode,
            address?.Latitude,
            address?.Longitude,
            representative?.Phone,
            representative?.Email,
            representative is null ? null : representative.UserId.HasValue,
            s.CreatedAt,
            s.UpdatedAt);
    }
}
