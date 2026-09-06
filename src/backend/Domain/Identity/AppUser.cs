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

    /// <summary>
    /// SCR-010. When the user chose their own interface language, or null if they never have.
    ///
    /// <para>A timestamp rather than a bool, and a separate column rather than making
    /// <see cref="Language"/> nullable. The default "ar" is indistinguishable from a deliberate choice
    /// of Arabic, so there was no way to tell a first-time user from one who picked the default -
    /// which is the only thing a first-run screen needs to know. Making Language nullable instead
    /// would have pushed a null through every consumer that renders a locale, to answer a question
    /// none of them asks.</para>
    /// </summary>
    public DateTimeOffset? LanguageChosenAt { get; set; }
    public bool IsActive { get; set; } = true;
}
