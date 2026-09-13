// An administrator's own wording for one transactional email, replacing the words the product
// shipped with.
//
// Same shape as UiStringOverride and the notification template, and for the same reason: an
// absent row means the shipped words, so nothing is seeded and a fresh deployment sends exactly
// what it was built to send.
//
// What makes email different from the other two is that a token dropped from the wording can
// lock somebody out rather than just read badly. An invitation without its link is an invitation
// nobody can accept. So the list of tokens a template must keep is checked when this row is
// written, not when the email is sent.
//
// Key is unique: one override per template, with both languages in one row. A subject in Arabic
// and a body in English is not a state anyone wants to reach halfway through saving.
//
// RowVersion refuses two administrators rewording the same email at once.

namespace MotsSupplierPortal.Domain.Configuration;

using MotsSupplierPortal.Domain.Common;

public sealed class EmailTemplateOverride : IVersionedAggregate
{
    public Guid Id { get; init; }

    public required string Key { get; init; }

    public required string SubjectAr { get; set; }
    public required string SubjectEn { get; set; }
    public required string BodyAr { get; set; }
    public required string BodyEn { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; private set; }
}
