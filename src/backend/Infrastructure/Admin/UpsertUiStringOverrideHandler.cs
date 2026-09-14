// Creating or replacing one rewording of an interface string.
//
// One operation rather than separate create and update routes. The identity of the thing is the key and the
// language, and an administrator editing a label does not know or care whether a row already exists. Asking them
// to would be exposing the storage.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

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
