// Finding a supplier's email when it is not on the supplier.
//
// WHY THIS IS NEEDED AT ALL. In this ERP a supplier's email lives on a separate Contact record, and it only
// appears on the supplier itself once somebody sets that contact as the supplier's PRIMARY contact - a field on
// the supplier, distinct from the "Is Primary Contact" box on the contact, which is the one people tick. The
// difference is invisible in the interface and easy to get wrong: it was got wrong on the test instance by
// somebody who administers the system, which is the strongest evidence available that the real data will have it
// wrong too.
//
// An import that read only the supplier's own field would have reported eighty suppliers with no email and given
// every one a placeholder login that cannot receive mail, when the truth is "the addresses are one table over". That
// is a wrong answer delivered confidently, and it would have sent somebody to ask Seven Gates for a data cleanup they
// do not need.
//
// THE SUPPLIER'S OWN VALUE WINS when it is there. It is the field their staff maintain deliberately, and a
// contact that disagrees with it is a stale row rather than a correction.
//
// A PRIMARY CONTACT BEATS A NON-PRIMARY ONE, and after that the order is by name. The tie-break is not
// decoration: a supplier with two contacts and no primary would otherwise take whichever the database returned
// first, and that is not a promise - the same defect the ministry feed already had, where one supplier's address
// read "Line 2" in one representation and "Line 0" in another, seconds apart. A stable wrong answer can be
// noticed; an unstable one cannot.
//
// EMAIL AND PHONE ARE RESOLVED INDEPENDENTLY. A supplier can carry one and not the other, and taking both from
// whichever contact happened to supply the email would drop a phone number that was sitting right there.
//
// A CONTACT WITH NO EMAIL IS NOT USELESS - it may still carry the phone - so it is skipped for one pass and
// considered for the other rather than filtered out at the top.
//
// THE CONTACT'S NAME BECOMES THE PERSON, but only when it is a person. The ERP creates a contact of its own when a
// supplier is saved with a phone, and names it "<supplier> Contact"; on the real server a quarter of the contacts are
// those. Taking that as a representative's name would address letters to "AL-OMAR CO Contact". So a name that is the
// supplier's own name, with or without " Contact", or its identifier, counts as no person at all - and the supplier's
// representative is then named after the company, which the admission says in its notes.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using MotsSupplierPortal.Application.Integration;

public sealed record ErpSupplierContact(
    string ContactName,
    string SupplierName,
    string? Email,
    string? Phone,
    bool IsPrimary,
    string? FullName = null);

public static class ErpContactMerge
{
    public static IReadOnlyList<ErpSupplier> Fill(
        IReadOnlyList<ErpSupplier> suppliers,
        IReadOnlyList<ErpSupplierContact> contacts)
    {
        if (contacts.Count == 0) return suppliers;

        var bySupplier = contacts
            .GroupBy(contact => contact.SupplierName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(contact => contact.IsPrimary)
                    .ThenBy(contact => contact.ContactName, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        return [.. suppliers.Select(supplier => Fill(supplier, bySupplier))];
    }

    private static ErpSupplier Fill(
        ErpSupplier supplier,
        IReadOnlyDictionary<string, List<ErpSupplierContact>> bySupplier)
    {
        if (!bySupplier.TryGetValue(supplier.ExternalId, out var candidates)) return supplier;

        return supplier with
        {
            Email = supplier.Email ?? candidates.Select(c => c.Email).FirstOrDefault(e => e is not null),
            Phone = supplier.Phone ?? candidates.Select(c => c.Phone).FirstOrDefault(p => p is not null),
            ContactPersonName = candidates
                .Select(c => c.FullName?.Trim())
                .FirstOrDefault(name => IsPerson(name, supplier)),
        };
    }

    public static bool IsPerson(string? name, ErpSupplier supplier)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var own = supplier.Name?.Trim() ?? supplier.ExternalId;

        return !string.Equals(name, own, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, own + " Contact", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, supplier.ExternalId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, supplier.ExternalId + " Contact", StringComparison.OrdinalIgnoreCase);
    }
}
