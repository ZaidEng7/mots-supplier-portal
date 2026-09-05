using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Configuration;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Domain.ReferenceData;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Configuration;

/// <summary>
/// FR-ADM-006/T-060. Reads and writes the settings catalogue.
///
/// <para><b>The catalogue is the authority, not the table.</b> A row whose key is not in
/// <see cref="SystemSettings.All"/> is not returned and cannot be written: a settings table that
/// accepts arbitrary keys is a key-value store nobody consumes, and the first typo becomes a setting
/// that looks configured and changes nothing.</para>
///
/// <para><b>Every write is audited</b> with the old and the new value. These settings decide whether
/// the public can register at all and when suppliers are warned about expiry, so "who closed
/// registration, and when" is a governance question.</para>
/// </summary>
public sealed class SystemSettingAdminHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ISystemSettingAdminHandler
{
    public async Task<IReadOnlyList<SystemSettingDto>> ListAsync(CancellationToken ct)
    {
        var stored = await StoredAsync(ct);

        // Catalogue order, not table order: the screen lists what CAN be configured, including the
        // settings nobody has touched. A list built from the rows would show an empty screen on a
        // fresh deployment, which reads as "there is nothing to configure".
        return [.. SystemSettings.All.Select(definition => ToDto(definition, stored.GetValueOrDefault(definition.Key)))];
    }

    public async Task<SystemSettingResult> UpdateAsync(UpdateSystemSettingCommand command, CancellationToken ct)
    {
        var definition = SystemSettings.Find(command.Key);
        if (definition is null) return new SystemSettingResult.UnknownKey();

        var value = command.Value?.Trim() ?? string.Empty;
        if (definition.Validate(value) is { } reason) return new SystemSettingResult.Invalid(reason);

        // Reference codes are checked against the live table, not just for shape. A default currency
        // pointing at a code that has been deactivated is a proposal form the supplier cannot submit,
        // and D-28 makes deactivation the normal way codes leave the catalogue - so this is a case
        // that WILL occur, not a defensive check.
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
