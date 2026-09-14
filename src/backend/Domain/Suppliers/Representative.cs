// A named person authorised to act for the supplier.
//
// Exactly one of them is the primary at all times, and the primary is the supplier's administrator.
//
// UserId links the person to a sign-in account once they have one.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class Representative
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public Guid? UserId { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Position { get; set; }
    public bool IsPrimary { get; set; }
}
