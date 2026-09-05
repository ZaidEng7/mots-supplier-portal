using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Domain.Notifications;

/// <summary>
/// FR-ADM-007/T-061. An administrator's override of one notification type's authored copy.
///
/// <para><b>An override, not the copy itself.</b> The shipped catalogue
/// (<c>NotificationCatalogue.jsonc</c>) stays the default and the fallback: a type with no row here
/// renders exactly what shipped. That is what makes the table safe to add - no deployment's wording
/// changes until somebody changes it, and deleting an override restores the shipped words rather than
/// leaving a notification with no text.</para>
///
/// <para>This is also why DELETE exists here and does not on reference data (D-28): a reference code
/// is the foreign key in live rows, whereas an override is a layer over something that still exists
/// underneath it.</para>
/// </summary>
public sealed class NotificationTemplate : IVersionedAggregate
{
    public Guid Id { get; init; }

    /// <summary>A <c>NotificationTypes</c> value. Unique - one override per type.</summary>
    public required string Type { get; init; }

    public required string TitleAr { get; set; }
    public required string TitleEn { get; set; }
    public required string BodyAr { get; set; }
    public required string BodyEn { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>§8.1. Two administrators rewording the same notification at once is worth refusing
    /// rather than resolving in favour of whoever saved second.</summary>
    public uint RowVersion { get; private set; }
}
