namespace MotsSupplierPortal.Application.Configuration;

/// <summary>
/// FR-ADM-006/T-060: the admin surface for system settings.
///
/// <para>Each item carries its own rules - kind, allowed values, bounds - so the screen renders the
/// right control and refuses the wrong value without a second copy of the catalogue on the client.
/// A settings screen that lets an administrator type "thirty" into a day count is worse than the
/// constant it replaced.</para>
/// </summary>
public sealed record SystemSettingDto(
    string Key,
    string Kind,
    string Value,
    string DefaultValue,
    /// <summary>False when no row exists: the value shown is the deployment's configuration or the
    /// built-in default. "Nobody has decided" and "an administrator chose this" are different facts,
    /// and only the second one has an author and a date.</summary>
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
    /// <summary><paramref name="Reason"/> is machine-readable (value_not_allowed, value_out_of_range,
    /// value_has_duplicates, value_required, reference_code_not_active) so the screen can say which
    /// rule was broken instead of "invalid".</summary>
    public sealed record Invalid(string Reason) : SystemSettingResult;
}

public interface ISystemSettingAdminHandler
{
    Task<IReadOnlyList<SystemSettingDto>> ListAsync(CancellationToken ct);

    Task<SystemSettingResult> UpdateAsync(UpdateSystemSettingCommand command, CancellationToken ct);

    /// <summary>The allow-listed subset a supplier or an unauthenticated visitor may read - see
    /// SystemSettings.PubliclyReadable.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadPublicAsync(CancellationToken ct);
}
