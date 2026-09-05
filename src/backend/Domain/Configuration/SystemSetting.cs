using System.Globalization;
using MotsSupplierPortal.Domain.Common;

namespace MotsSupplierPortal.Domain.Configuration;

/// <summary>What kind of value a setting holds, so the admin surface can validate before storing
/// rather than discovering the problem in the job that reads it three hours later.</summary>
public enum SettingKind
{
    /// <summary>One of <see cref="SettingDefinition.AllowedValues"/>.</summary>
    Choice,

    /// <summary>A whole number inside <see cref="SettingDefinition.Minimum"/>..<see cref="SettingDefinition.Maximum"/>.</summary>
    Integer,

    /// <summary>A comma-separated list of whole numbers, each inside the same bounds.</summary>
    IntegerList,

    /// <summary>A reference-data code. <see cref="SettingDefinition.ReferenceTable"/> names the table
    /// it must exist and be active in - which the store checks, because a default currency pointing
    /// at a deactivated code is a form the supplier cannot submit.</summary>
    ReferenceCode,
}

/// <summary>
/// One configurable system setting: its key, what a valid value looks like, and what the system does
/// when nobody has set one.
///
/// <para>The definition carries the validation because there is exactly one admin write path and a
/// setting whose bounds live at the consumer is a setting the admin screen will happily corrupt.</para>
/// </summary>
public sealed record SettingDefinition(
    string Key,
    SettingKind Kind,
    string DefaultValue,
    string[]? AllowedValues = null,
    int? Minimum = null,
    int? Maximum = null,
    string? ReferenceTable = null)
{
    /// <summary>Null when <paramref name="value"/> is acceptable; otherwise a machine-readable reason.
    /// Reference-code existence is NOT checked here - the domain has no store - see the handler.</summary>
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
                // A ladder with a repeated rung would send the same reminder twice, and the reminder
                // ledger keys on the threshold, so the second send would be suppressed silently -
                // the setting would look accepted and behave differently from what it says.
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

/// <summary>
/// FR-ADM-006's settings, as the catalogue of what is actually configurable.
///
/// <para><b>Three of FR-ADM-006's five are here; two are deliberately absent</b> (D-33). The numeral
/// system is not a system setting: R-1 makes numerals a property of the locale - Arabic renders
/// Eastern Arabic numerals, English renders Latin - and a global override would let an administrator
/// put the wrong numerals under the wrong language for every user at once. The approval hierarchy is
/// not a setting either: <c>RfqApproval</c> stores an ordered step list and encodes no
/// amount-threshold routing, so "configure the hierarchy" is a feature with its own state machine,
/// not a value in a table. Both are recorded in BACKLOG-REMEDIATION.md rather than half-built here.</para>
/// </summary>
public static class SystemSettings
{
    /// <summary>FR-REG-002. <c>open</c> is the default the requirement itself names.</summary>
    public const string RegistrationMode = "registration.mode";

    public const string RegistrationOpen = "open";
    public const string RegistrationClosed = "closed";

    /// <summary>BR-18's SYP default, as a value rather than a seed row nobody can change.</summary>
    public const string DefaultCurrencyCode = "proposals.defaultCurrencyCode";

    /// <summary>BRULE-021/FR-DOC-006. Was reachable only through appsettings, which is a deploy.</summary>
    public const string ExpiringSoonWindowDays = "documents.expiringSoonWindowDays";

    /// <summary>BRULE-025's ladder, same story.</summary>
    public const string RenewalReminderDays = "documents.renewalReminderDays";

    public static readonly SettingDefinition[] All =
    [
        new(RegistrationMode, SettingKind.Choice, RegistrationOpen,
            AllowedValues: [RegistrationOpen, RegistrationClosed]),
        new(DefaultCurrencyCode, SettingKind.ReferenceCode, "SYP", ReferenceTable: "currencies"),
        // 365 rather than unbounded: a window longer than a year would mark every document with an
        // expiry date as expiring, which reads as a broken portal rather than a strict one.
        new(ExpiringSoonWindowDays, SettingKind.Integer, "30", Minimum: 1, Maximum: 365),
        new(RenewalReminderDays, SettingKind.IntegerList, "30,14,3", Minimum: 1, Maximum: 365),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>The subset an unauthenticated or supplier-facing client may read. An allow-list, not
    /// a filter on the table: a setting added later is invisible to the public read until someone
    /// decides it should not be, which is the direction that fails safely.</summary>
    public static readonly string[] PubliclyReadable = [RegistrationMode, DefaultCurrencyCode];
}

/// <summary>
/// FR-ADM-006. One row per <see cref="SystemSettings"/> key, holding the administrator's value.
///
/// <para>Absent row means "nobody has decided": the consumer falls back to configuration and then to
/// <see cref="SettingDefinition.DefaultValue"/>, so an environment that has never opened this screen
/// behaves exactly as it did before the table existed.</para>
/// </summary>
public sealed class SystemSetting : IVersionedAggregate
{
    public Guid Id { get; init; }
    public required string Key { get; init; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>§8.1. Two administrators changing the reminder ladder at once is the case worth
    /// refusing rather than resolving in favour of whoever saved second.</summary>
    public uint RowVersion { get; private set; }
}
