// The settings an administrator can change from the admin screen, what a valid value looks like
// for each, and the value somebody has actually chosen.
//
// Three types live in this file.
//
// SettingKind says what kind of value a setting holds, so the admin screen can refuse a bad one
// before storing it rather than the job that reads it three hours later discovering the problem.
// Choice is one of a fixed list. Integer is a whole number inside a range. IntegerList is a
// comma-separated list of whole numbers, each inside that same range. ReferenceCode is a code
// from a reference-data table; the setting names the table, and the code must exist and be active
// in it, because a default currency pointing at a deactivated code is a form the supplier cannot
// submit.
//
// SettingDefinition is one setting's key, its kind, its default, and whichever bounds its kind
// needs. The definition carries the validation rather than the code that reads the setting,
// because there is exactly one admin write path, and a setting whose bounds live at the far end
// is a setting the admin screen will happily corrupt.
//
// Validate returns null when the value is acceptable, and otherwise a short reason the interface
// can translate. One check is deliberately absent: whether a reference code exists. The domain
// has no database, so that one belongs to the handler.
//
// Validate also refuses a reminder ladder with the same number twice. A repeated rung would send
// the same reminder twice, and the reminder ledger records one send per threshold, so the second
// would be dropped without a word. The setting would look accepted and behave differently from
// what it says.
//
// SystemSettings is the catalogue of what is actually configurable. Five settings were asked for
// and three are here; the other two are deliberately absent, and this is where that is recorded.
//
// The numeral system is not a system setting. Numerals belong to the language: Arabic renders
// Eastern Arabic numerals, English renders Latin ones. A global override would let one
// administrator put the wrong numerals under the wrong language for every user at once.
//
// The approval hierarchy is not a setting either. Approval on a tender stores an ordered list of
// steps and knows nothing about routing by amount, so "configure the hierarchy" is a feature with
// its own state machine, not a value in a table.
//
// Both are written down in the remediation backlog rather than half-built here.
//
// The three that are here:
//
//   registration.mode                  open or closed; open is the default the requirement names
//   proposals.defaultCurrencyCode      SYP, as a value rather than a seed row nobody can change
//   documents.expiringSoonWindowDays   how early a document counts as expiring soon
//   documents.renewalReminderDays      the ladder of days before expiry that send a reminder
//   review.slaWorkingDays              the onboarding review target, in working days
//
// The last three were previously reachable only through the deployment's own configuration file,
// which means a deploy to change a number.
//
// The expiring-soon window stops at 365 rather than running unbounded. A window longer than a
// year would mark every document that has an expiry date as expiring, which reads as a broken
// portal rather than a strict one.
//
// The review target is five working days, bounded at 60. A target measured in months is not a
// target, and a zero-day one would put every case in the queue past its date the moment it
// arrived. The written process starts, pauses and resumes a timer across submission, review,
// information requested and resubmission, and never names a number, so the honest options were no
// timer at all or a stated default somebody can change. This is the second one, and it is shown
// as a target date and never as a breach, so the product does not claim a commitment the ministry
// has not made.
//
// PubliclyReadable is the subset a client may read before signing in. It is a list of what is
// allowed out rather than a filter on the table, so a setting added later is invisible to the
// public read until somebody decides it should not be. That is the direction that fails safely.
//
// SystemSetting is the row itself, one per key, holding the administrator's value. An absent row
// means nobody has decided: the code that reads the setting falls back to the deployment's
// configuration and then to the definition's default, so an environment where this screen has
// never been opened behaves exactly as it did before the table existed.
//
// RowVersion refuses two administrators changing the same setting at once.

namespace MotsSupplierPortal.Domain.Configuration;

using System.Globalization;
using MotsSupplierPortal.Domain.Common;

public enum SettingKind
{
    Choice,

    Integer,

    IntegerList,

    ReferenceCode,
}

public sealed record SettingDefinition(
    string Key,
    SettingKind Kind,
    string DefaultValue,
    string[]? AllowedValues = null,
    int? Minimum = null,
    int? Maximum = null,
    string? ReferenceTable = null)
{
    public string? Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "value_required";

        switch (Kind)
        {
            case SettingKind.Choice:
                return AllowedValues!.Contains(value, StringComparer.Ordinal) ? null : "value_not_allowed";

            case SettingKind.Integer:
                return ParseBounded(value) is null ? "value_out_of_range" : null;

            case SettingKind.IntegerList:
                var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) return "value_required";
                if (parts.Any(p => ParseBounded(p) is null)) return "value_out_of_range";
                return parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).Distinct().Count() == parts.Length
                    ? null
                    : "value_has_duplicates";

            case SettingKind.ReferenceCode:
                return value.Trim().Length == value.Length ? null : "value_required";

            default:
                return "value_not_allowed";
        }
    }

    private int? ParseBounded(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return null;
        if (Minimum is { } min && parsed < min) return null;
        if (Maximum is { } max && parsed > max) return null;
        return parsed;
    }
}

public static class SystemSettings
{
    public const string RegistrationMode = "registration.mode";

    public const string RegistrationOpen = "open";
    public const string RegistrationClosed = "closed";

    public const string DefaultCurrencyCode = "proposals.defaultCurrencyCode";

    public const string ExpiringSoonWindowDays = "documents.expiringSoonWindowDays";

    public const string RenewalReminderDays = "documents.renewalReminderDays";

    public const string ReviewSlaWorkingDays = "review.slaWorkingDays";

    public static readonly SettingDefinition[] All =
    [
        new(RegistrationMode, SettingKind.Choice, RegistrationOpen,
            AllowedValues: [RegistrationOpen, RegistrationClosed]),
        new(DefaultCurrencyCode, SettingKind.ReferenceCode, "SYP", ReferenceTable: "currencies"),
        new(ExpiringSoonWindowDays, SettingKind.Integer, "30", Minimum: 1, Maximum: 365),
        new(RenewalReminderDays, SettingKind.IntegerList, "30,14,3", Minimum: 1, Maximum: 365),
        new(ReviewSlaWorkingDays, SettingKind.Integer, "5", Minimum: 1, Maximum: 60),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    public static readonly string[] PubliclyReadable = [RegistrationMode, DefaultCurrencyCode];
}

public sealed class SystemSetting : IVersionedAggregate
{
    public Guid Id { get; init; }
    public required string Key { get; init; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; private set; }
}
