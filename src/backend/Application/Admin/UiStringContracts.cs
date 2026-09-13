// The vocabulary for the interface-label overrides: one rewording, and the whole set for one language.
//
// The public read returns every override for a language as a flat set, so the interface merges it over its own
// bundle.
//
// It is open to anybody, and it has to be. The sign-in screen, the registration form and the first-run
// interstitial all render before anyone has signed in, and a reworded label that only appeared afterwards
// would be a worse inconsistency than no rewording at all.
//
// Nothing here is sensitive. These are interface labels, and the shipped ones are already in a bundle any
// visitor can read.

namespace MotsSupplierPortal.Application.Admin;

public sealed record UiStringOverrideDto(string Key, string Language, string Value, DateTimeOffset UpdatedAt);

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
    Task<bool> HandleAsync(string key, string language, CancellationToken ct);
}
