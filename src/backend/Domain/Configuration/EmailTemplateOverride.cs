using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Domain.Configuration;

/// <summary>
/// T-076. An administrator's wording for one transactional email, replacing the shipped copy.
///
/// <para>Same shape as <see cref="UiStringOverride"/> and notification_template, and for the same reason:
/// an absent row means the shipped words, so nothing is seeded and a fresh deployment sends exactly what it
/// was built to send.</para>
///
/// <para><b>What makes email different from the other two</b> is that a token dropped from the wording can
/// be a lockout rather than a readability problem - an invitation without its link is an invitation nobody
/// can accept - so the required-token contract is enforced when this row is WRITTEN. See
/// EmailTemplateKeys.All.</para>
/// </summary>
public sealed class EmailTemplateOverride : IVersionedAggregate
{
    public Guid Id { get; init; }

    /// <summary>An EmailTemplateKeys value. Unique - one override per template, both locales in one row,
    /// because a subject in Arabic and a body in English is not a state anyone wants to reach halfway
    /// through saving.</summary>
    public required string Key { get; init; }

    public required string SubjectAr { get; set; }
    public required string SubjectEn { get; set; }
    public required string BodyAr { get; set; }
    public required string BodyEn { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>§8.1: two administrators rewording the same email at once is worth refusing.</summary>
    public uint RowVersion { get; private set; }
}
