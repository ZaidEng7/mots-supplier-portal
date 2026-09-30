// How long a value from the ERP may be before the portal's columns refuse it.
//
// THE ERP ALLOWS LONGER VALUES THAN THE PORTAL STORES in several places - a supplier's details field has no limit at
// all, and its group, city and custom fields allow 140 characters where the portal keeps 100 or 50. A value one
// character too long does not fail quietly: the database refuses the whole supplier. So the values below are measured
// first, by ErpImportAdmission, ErpRegistrationNumbers and ErpAddressMapper, where the preview sees the outcome too;
// Name is applied to the Arabic name. The English name, tax number, email and phone are not measured, and one of those
// too long for its column fails that supplier, which the run reports as failed before going on to the next.
//
// TEXT IS CUT, CODES ARE DROPPED. A description or a street cut short is still most of the truth, and says so in a
// note. A registration number, a registration type or a group name cut short is a different value that looks right,
// so those are left empty instead, with a note saying what the ERP held.
//
// The numbers must equal the column lengths in the EF configuration; ErpFieldLimitsTests compares them with the model.

namespace MotsSupplierPortal.Application.Integration;

public static class ErpFieldLimits
{
    public const int Name = 200;
    public const int Description = 2000;
    public const int SupplierGroup = 100;
    public const int RegistrationNumber = 100;
    public const int RegistrationType = 50;
    public const int PersonName = 200;
    public const int AddressLine = 300;
    public const int City = 100;

    public static string? Cut(string? value, int limit, string field, List<string> notes)
    {
        if (value is null || value.Length <= limit) return value;

        notes.Add($"The ERP's {field} is {value.Length} characters; the portal keeps the first {limit}.");
        return value[..limit].TrimEnd();
    }

    public static string? DropIfTooLong(string? value, int limit, string field, List<string> notes)
    {
        if (value is null || value.Length <= limit) return value;

        notes.Add($"The ERP's {field} is {value.Length} characters, longer than the portal's {limit}; left empty "
                  + "rather than cut into a different value.");
        return null;
    }
}
