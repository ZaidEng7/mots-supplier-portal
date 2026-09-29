// What the ERP knows about a supplier beyond its name, tax number and contact, carried into the portal as it stands.
//
// Every value is null when the ERP has none, and a null never overwrites what the portal already holds: see
// Supplier.ApplyErpSnapshot.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed record ErpSupplierDetails(
    string? DisplayNameAr,
    string? RegistrationNumber,
    string? RegistrationType,
    string? SupplierGroup,
    string? Description);
