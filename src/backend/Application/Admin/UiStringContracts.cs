namespace MotsSupplierPortal.Application.Admin;

/// <summary>SCR-716. One administrator rewording.</summary>
public sealed record UiStringOverrideDto(string Key, string Language, string Value, DateTimeOffset UpdatedAt);

/// <summary>
/// The public read: every override for one language, flat, so the SPA can merge it over its bundle.
///
/// <para><b>Anonymous, and it has to be.</b> The login screen, the registration form and SCR-047's
/// interstitial all render before anyone is authenticated, and a reworded label that only appeared after
/// sign-in would be a worse inconsistency than no rewording at all. Nothing here is sensitive: these are
/// interface labels, and the shipped ones are already in a JavaScript bundle any visitor can read.</para>
/// </summary>
public sealed record UiStringBundleDto(string Language, IReadOnlyDictionary<string, string> Strings);

public sealed record UpsertUiStringCommand(string Key, string Language, string Value, Guid ActorUserId);

public interface IGetUiStringBundleHandler
{
    Task<UiStringBundleDto> HandleAsync(string language, CancellationToken ct);
}

public interface IListUiStringOverridesHandler
{
    Task<IReadOnlyList<UiStringOverrideDto>> HandleAsync(CancellationToken ct);
}

public interface IUpsertUiStringOverrideHandler
{
    Task<UiStringOverrideDto> HandleAsync(UpsertUiStringCommand command, CancellationToken ct);
}

public interface IDeleteUiStringOverrideHandler
{
    /// <summary>False when there was no override to remove. Deleting one restores the shipped string,
    /// which is the only way back - there is no "reset" beyond this.</summary>
    Task<bool> HandleAsync(string key, string language, CancellationToken ct);
}
