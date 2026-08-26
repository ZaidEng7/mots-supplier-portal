using Microsoft.AspNetCore.Identity;

namespace MotsSupplierPortal.Domain.Identity;

/// <summary>
/// ASP.NET Core Identity user, extended with the membership-scope fields that drive row-scoping
/// (docs/architecture/DOMAIN-MODEL.md §5.1). A user belongs to exactly one principal context:
/// SupplierId (supplier-side) XOR OrganizationId (back-office/ministry) XOR neither (platform admin).
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public required string FullName { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? OrgUnitId { get; set; }
    public string Language { get; set; } = "ar";
    public bool IsActive { get; set; } = true;
}
