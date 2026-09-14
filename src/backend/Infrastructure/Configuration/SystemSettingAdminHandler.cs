// Reads and writes the system settings.
//
// The catalogue is the authority, not the table. A row whose key is not in the catalogue is neither
// returned nor writable: a settings table that accepts arbitrary keys is a store nobody consumes, and the
// first typo becomes a setting that looks configured and changes nothing.
//
// Every write is audited with the old value and the new one. These settings decide whether the public can
// register at all and when suppliers are warned about expiry, so who closed registration and when is a
// governance question.

namespace MotsSupplierPortal.Infrastructure.Configuration;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Configuration;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class SystemSettingAdminHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ISystemSettingAdminHandler
{
    public async Task<IReadOnlyList<SystemSettingDto>> ListAsync(CancellationToken ct)
    {
        var stored = await StoredAsync(ct);

        return [.. SystemSettings.All.Select(definition => ToDto(definition, stored.GetValueOrDefault(definition.Key)))];
    }

    public async Task<SystemSettingResult> UpdateAsync(UpdateSystemSettingCommand command, CancellationToken ct)
    {
        var definition = SystemSettings.Find(command.Key);
        if (definition is null) return new SystemSettingResult.UnknownKey();

        var value = command.Value?.Trim() ?? string.Empty;
        if (definition.Validate(value) is { } reason) return new SystemSettingResult.Invalid(reason);

        if (definition.Kind is SettingKind.ReferenceCode
            && !await db.Set<Currency>().AnyAsync(c => c.Code == value && c.IsActive, ct))
        {
            return new SystemSettingResult.Invalid("reference_code_not_active");
        }

        var existing = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == command.Key, ct);
        var previous = existing?.Value ?? "(unset)";

        if (existing is null)
        {
            existing = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                Key = definition.Key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedByUserId = scope.UserId,
            };
            db.Add(existing);
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedByUserId = scope.UserId;
        }

        await auditLogger.LogAsync("SystemSetting", existing.Id, "setting.updated", scope.UserId,
            referenceCode: definition.Key, fromState: previous, toState: value, ct: ct);
        await db.SaveChangesAsync(ct);

        var stored = await StoredAsync(ct);
        return new SystemSettingResult.Success(ToDto(definition, stored.GetValueOrDefault(definition.Key)));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadPublicAsync(CancellationToken ct)
    {
        var stored = await StoredAsync(ct);

        return SystemSettings.PubliclyReadable
            .Select(SystemSettings.Find)
            .Where(d => d is not null)
            .ToDictionary(d => d!.Key, d => stored.GetValueOrDefault(d!.Key)?.Value ?? d.DefaultValue, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, SystemSetting>> StoredAsync(CancellationToken ct)
    {
        var keys = SystemSettings.All.Select(d => d.Key).ToArray();
        var rows = await db.Set<SystemSetting>().AsNoTracking().Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        return rows.ToDictionary(s => s.Key, StringComparer.Ordinal);
    }

    private static SystemSettingDto ToDto(SettingDefinition definition, SystemSetting? stored) =>
        new(definition.Key,
            definition.Kind.ToString(),
            stored?.Value ?? definition.DefaultValue,
            definition.DefaultValue,
            stored is not null,
            stored?.UpdatedAt,
            definition.AllowedValues,
            definition.Minimum,
            definition.Maximum);
}
