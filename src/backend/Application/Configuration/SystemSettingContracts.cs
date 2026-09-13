// The vocabulary for the system-settings screen.
//
// Each setting carries its own rules with it: what kind of value it holds, which values are allowed, and
// what bounds apply. The screen therefore renders the right control and refuses the wrong value without
// keeping a second copy of the catalogue. A settings screen that lets an administrator type a word into
// a day count is worse than the constant it replaced.
//
// IsOverridden is false when no row exists, meaning the value shown is the deployment's own
// configuration or the built-in default. "Nobody has decided" and "an administrator chose this" are
// different facts, and only the second has an author and a date.
//
// An unknown key is its own outcome, separate from an invalid value, because the caller asked for
// something that does not exist rather than sending something wrong.
//
// An invalid value carries a machine-readable reason rather than the word invalid, so the screen can say
// which rule was broken: not an allowed value, out of range, a repeated entry, empty, or a reference
// code that is not active.
//
// ReadPublicAsync is the small allowed subset a supplier or a visitor who has not signed in may read.

namespace MotsSupplierPortal.Application.Configuration;

public sealed record SystemSettingDto(
    string Key,
    string Kind,
    string Value,
    string DefaultValue,
    bool IsOverridden,
    DateTimeOffset? UpdatedAt,
    string[]? AllowedValues,
    int? Minimum,
    int? Maximum);

public sealed record UpdateSystemSettingCommand(string Key, string Value);

public abstract record SystemSettingResult
{
    public sealed record Success(SystemSettingDto Setting) : SystemSettingResult;
    public sealed record UnknownKey : SystemSettingResult;
    public sealed record Invalid(string Reason) : SystemSettingResult;
}

public interface ISystemSettingAdminHandler
{
    Task<IReadOnlyList<SystemSettingDto>> ListAsync(CancellationToken ct);

    Task<SystemSettingResult> UpdateAsync(UpdateSystemSettingCommand command, CancellationToken ct);

    Task<IReadOnlyDictionary<string, string>> ReadPublicAsync(CancellationToken ct);
}
