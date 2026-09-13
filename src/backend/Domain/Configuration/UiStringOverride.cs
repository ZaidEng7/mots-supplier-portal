// One interface label an administrator has reworded, replacing the shipped text at runtime.
//
// Every word in the product is bundled with the web app, so correcting a single label used to
// mean a code change and a deployment. That is the wrong shape for wording: the people who own
// the words are not the people who own releases.
//
// Modelled on the notification template deliberately, because it is the same idea one layer out:
// the shipped catalogue is the default and a row here is the administrator's version. It is not
// the same table. Notification wording is rendered on the server, filled with tokens and sent to
// people who are not looking at a screen; these are labels on a screen. Merging the two would put
// one row type in charge of two unrelated rendering paths.
//
// Key is the path the web app uses for the label, such as proposal.revise. It is not checked
// against a list of known keys, and that is not laxity: the server does not hold the web app's
// key set, so any check here would be a second, always-stale copy of it. The admin screen offers
// the real keys instead, read from the bundle it is running, which is the only place that knows
// them. An override for a key nothing renders is inert rather than harmful.
//
// Language is "ar" or "en", the two the product ships. One row per key per language, because a
// rewording in one language is not a rewording in the other.
//
// RowVersion refuses two administrators rewording the same label at once.

namespace MotsSupplierPortal.Domain.Configuration;

using MotsSupplierPortal.Domain.Common;

public sealed class UiStringOverride : IVersionedAggregate
{
    public Guid Id { get; init; }

    public required string Key { get; init; }

    public required string Language { get; init; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; private set; }
}
