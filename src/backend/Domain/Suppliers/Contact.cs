namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>FR-PROF-004/STORY-04.4.1: a non-representative contact (e.g. finance, technical) -
/// distinct from Representative, which carries the "authorized to act for the supplier / exactly
/// one primary" invariant. A Contact is informational only, no authority implication.</summary>
public sealed class Contact
{
    public Guid Id { get; init; }
    public Guid SupplierId { get; init; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
}
