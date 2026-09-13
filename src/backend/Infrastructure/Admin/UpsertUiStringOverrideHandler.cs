using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Admin;

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
