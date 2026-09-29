// Choosing which of a supplier's ERP addresses is the one the portal takes.
//
// A DISABLED ADDRESS IS NEVER CHOSEN. The ERP keeps an address a supplier moved away from, switched off, beside the
// new one - and with nothing marked primary the old one can sort first by name, which would file the supplier at a
// place it has left.
//
// ONE ADDRESS, CHOSEN IN A FIXED ORDER. The supplier's own primary-address field names one when somebody set it; after
// that an address ticked as primary, then a billing address, then the first by name. The last step matters as much as
// the first: without it a supplier with two unmarked addresses would take whichever the database returned first, and
// that changes between requests - the same unstable-answer defect ErpContactMerge describes.
//
// Whether the chosen address can become a portal address at all - it needs a street, a city and a Syrian governorate
// - is decided in ErpAddressMapper, where the import and the preview both see it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using MotsSupplierPortal.Application.Integration;

public sealed record ErpSupplierAddressRow(
    string AddressName,
    string SupplierName,
    string? Line1,
    string? Line2,
    string? City,
    string? Country,
    bool IsPrimary,
    string? AddressType,
    bool Disabled = false);

public static class ErpAddressMerge
{
    public static IReadOnlyList<ErpSupplier> Fill(
        IReadOnlyList<ErpSupplier> suppliers,
        IReadOnlyList<ErpSupplierAddressRow> addresses)
    {
        if (addresses.Count == 0) return suppliers;

        var bySupplier = addresses
            .Where(address => !address.Disabled)
            .GroupBy(address => address.SupplierName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        return [.. suppliers.Select(supplier =>
            bySupplier.TryGetValue(supplier.ExternalId, out var candidates)
                ? supplier with { Address = Chosen(supplier, candidates) }
                : supplier)];
    }

    private static ErpSupplierAddress Chosen(ErpSupplier supplier, List<ErpSupplierAddressRow> candidates)
    {
        var chosen = candidates
            .OrderByDescending(a => string.Equals(a.AddressName, supplier.PrimaryAddressName, StringComparison.Ordinal))
            .ThenByDescending(a => a.IsPrimary)
            .ThenByDescending(a => string.Equals(a.AddressType, "Billing", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.AddressName, StringComparer.Ordinal)
            .First();

        return new ErpSupplierAddress(chosen.Line1, chosen.Line2, chosen.City, chosen.Country);
    }
}
