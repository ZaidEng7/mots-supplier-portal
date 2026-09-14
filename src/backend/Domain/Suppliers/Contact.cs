// Somebody at the supplier worth contacting, such as a finance or technical person.
//
// This is not a representative. A representative is authorised to act for the supplier and exactly one
// of them is primary. A contact is informational and implies no authority at all.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class Contact
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
}
