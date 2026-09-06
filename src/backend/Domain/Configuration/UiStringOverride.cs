using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Domain.Configuration;

/// <summary>
/// SCR-716. One interface string an administrator has reworded, replacing the shipped copy at runtime.
///
/// <para><b>Why this exists.</b> Every string in the product is bundled in the SPA's `i18n/config.ts`,
/// so correcting a single word - and ARABIC-REVIEW.md is a long list of words awaiting exactly that -
/// meant a code change and a deployment. That is the wrong shape for copy: the people who own the
/// wording are not the people who own releases.</para>
///
/// <para>Modelled on NotificationTemplate, deliberately, because it is the same idea one layer out: the
/// shipped catalogue is the default and a row here is the administrator's version. It is NOT the same
/// table - notification copy is rendered server-side with tokens and sent to people who are not looking
/// at a screen, while these are interface labels - and merging the two would put one row type in charge
/// of two unrelated rendering paths.</para>
/// </summary>
public sealed class UiStringOverride : IVersionedAggregate
{
    public Guid Id { get; init; }

    /// <summary>The i18n key path as the SPA writes it, e.g. <c>proposal.revise</c>. Unique per language.
    ///
    /// <para><b>Not validated against a list of known keys, and that is not laxity.</b> The server does
    /// not hold the SPA's key set - it lives in the bundle - so any check here would be a second,
    /// always-stale copy of it. The admin screen offers the real keys instead, read from the bundle it is
    /// running, which is the only place that actually knows them. An override for a key nothing renders is
    /// inert rather than harmful.</para></summary>
    public required string Key { get; init; }

    /// <summary>"ar" or "en" - the two the product ships. One row per key per language, because a
    /// rewording in one language is not a rewording in the other.</summary>
    public required string Language { get; init; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>§8.1. Two administrators rewording the same label at once is worth refusing rather than
    /// resolving in favour of whoever saved second.</summary>
    public uint RowVersion { get; private set; }
}
