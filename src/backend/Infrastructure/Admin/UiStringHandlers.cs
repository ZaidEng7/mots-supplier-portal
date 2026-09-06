using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>SCR-716's public read - see UiStringBundleDto for why it is anonymous.</summary>
public sealed class GetUiStringBundleHandler(AppDbContext db) : IGetUiStringBundleHandler
{
    public async Task<UiStringBundleDto> HandleAsync(string language, CancellationToken ct)
    {
        var strings = await db.UiStringOverrides.AsNoTracking()
            .Where(o => o.Language == language)
            .ToDictionaryAsync(o => o.Key, o => o.Value, ct);

        // An empty dictionary is the normal answer, not a failure: no overrides means the shipped bundle
        // stands, which is what a fresh deployment should do.
        return new UiStringBundleDto(language, strings);
    }
}

public sealed class ListUiStringOverridesHandler(AppDbContext db) : IListUiStringOverridesHandler
{
    public async Task<IReadOnlyList<UiStringOverrideDto>> HandleAsync(CancellationToken ct) =>
        await db.UiStringOverrides.AsNoTracking()
            .OrderBy(o => o.Key).ThenBy(o => o.Language)
            .Select(o => new UiStringOverrideDto(o.Key, o.Language, o.Value, o.UpdatedAt))
            .ToListAsync(ct);
}

/// <summary>
/// Create or replace one rewording.
///
/// <para><b>Upsert rather than separate create and update routes.</b> The unique key is (key, language)
/// and an administrator editing a label does not know or care whether a row already exists - asking them
/// to would be exposing the storage. The identity of the thing is the key, not a generated id.</para>
/// </summary>
public sealed class UpsertUiStringOverrideHandler(AppDbContext db) : IUpsertUiStringOverrideHandler
{
    public async Task<UiStringOverrideDto> HandleAsync(UpsertUiStringCommand command, CancellationToken ct)
    {
        var existing = await db.UiStringOverrides
            .FirstOrDefaultAsync(o => o.Key == command.Key && o.Language == command.Language, ct);

        if (existing is null)
        {
            existing = new UiStringOverride
            {
                Id = Guid.CreateVersion7(),
                Key = command.Key,
                Language = command.Language,
                Value = command.Value,
            };
            db.UiStringOverrides.Add(existing);
        }
        else
        {
            existing.Value = command.Value;
        }

        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedByUserId = command.ActorUserId;
        await db.SaveChangesAsync(ct);

        return new UiStringOverrideDto(existing.Key, existing.Language, existing.Value, existing.UpdatedAt);
    }
}

public sealed class DeleteUiStringOverrideHandler(AppDbContext db) : IDeleteUiStringOverrideHandler
{
    public async Task<bool> HandleAsync(string key, string language, CancellationToken ct)
    {
        var existing = await db.UiStringOverrides
            .FirstOrDefaultAsync(o => o.Key == key && o.Language == language, ct);
        if (existing is null) return false;

        // A hard delete, and here it is right: the row IS the override, so removing it restores the
        // shipped string. Deactivating instead would leave a row that means nothing.
        db.UiStringOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
