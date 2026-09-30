// The one place the integration tests build an ERP supplier. The unit tests have their own, in their own project.
//
// EVERY FIELD IS SET BY NAME. ErpSupplier has twenty-one fields, most of them nullable strings, and a positional
// constructor lets a value one slot off compile into the wrong field. So Supplier gives the plainest record and a test
// states what it changes with a `with` expression; a field added to ErpSupplier is added here once, and a test that
// does not care about it never mentions it.
//
// THE PLAIN SUPPLIER is its identifier as its name, in the "Local" group, a company in Syria trading in SYP, not
// disabled and with no approval workflow - and nothing else: no tax number, email, phone, address or contact. A test
// that needs one of those sets it, so what a test depends on is written in the test.
//
// THE TIMESTAMPS ARE THE MOMENT OF THE CALL, as a real read's would be; the import does not read them.

namespace MotsSupplierPortal.Tests.Integration.Integration;

using MotsSupplierPortal.Application.Integration;

internal static class ErpSupplierTestFactory
{
    public static ErpSupplier Supplier(string externalId) => new(
        ExternalId: externalId,
        Name: externalId,
        SupplierGroup: "Local",
        LegalType: "Company",
        TaxId: null,
        Country: "Syria",
        Email: null,
        Phone: null,
        Disabled: false,
        Currency: "SYP",
        PrimaryAddressName: null,
        PrimaryContactName: null,
        CreatedAt: DateTimeOffset.UtcNow,
        ModifiedAt: DateTimeOffset.UtcNow);
}
