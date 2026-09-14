// An administrator's own wording for one notification type, replacing the words the product
// shipped with.
//
// An override, not the wording itself. The shipped catalogue stays the default and the fallback, so
// a type with no row here renders exactly what shipped. That is what makes the table safe to add:
// no deployment's wording changes until somebody changes it, and deleting an override restores the
// shipped words rather than leaving a notification with no text.
//
// It is also why deleting is allowed here and not on reference data. A reference code is pointed at
// by live rows, whereas an override is a layer over something that still exists underneath it.
//
// Type is unique: one override per notification type.
//
// RowVersion refuses two administrators rewording the same notification at once.

namespace MotsSupplierPortal.Domain.Notifications;

using MotsSupplierPortal.Domain.Common;

public sealed class NotificationTemplate : IVersionedAggregate
{
    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string TitleAr { get; set; }
    public required string TitleEn { get; set; }
    public required string BodyAr { get; set; }
    public required string BodyEn { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; private set; }
}
