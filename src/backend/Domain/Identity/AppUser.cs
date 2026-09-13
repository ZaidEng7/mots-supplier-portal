// A person who can sign in, with the fields that decide what rows they are allowed to see.
//
// A user belongs to exactly one context: a supplier, or a buying organization, or neither. Exactly
// one of SupplierId and OrganizationId is set for staff and supplier users; a platform
// administrator has neither.
//
// Language is the interface language, defaulting to Arabic.
//
// LanguageChosenAt is when the user picked their own language, or null if they never have. It is a
// timestamp rather than a yes-or-no, and a separate column rather than making Language nullable. The
// default of Arabic is indistinguishable from a deliberate choice of Arabic, so there was otherwise
// no way to tell a first-time user from one who picked the default, which is the only thing the
// first-run screen needs to know. Making Language nullable instead would have pushed a null through
// every screen that renders a language, to answer a question none of them asks.
//
// Everything else about signing in, passwords included, comes from the framework's own user type
// that this one extends.

namespace MotsSupplierPortal.Domain.Identity;

using Microsoft.AspNetCore.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public required string FullName { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? OrgUnitId { get; set; }
    public string Language { get; set; } = "ar";

    public DateTimeOffset? LanguageChosenAt { get; set; }
    public bool IsActive { get; set; } = true;
}
