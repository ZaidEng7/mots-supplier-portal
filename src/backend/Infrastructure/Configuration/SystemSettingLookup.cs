// Reads one setting for the code that consumes it.
//
// The order of precedence is the stored row, then the deployment's own configuration, then the setting's
// built-in default.
//
// Two of these settings were reachable only through the deployment's configuration before this table
// existed, and an environment that set them there did so on purpose. Letting the database win only when a
// row actually exists means the screen takes over a setting when an administrator touches it and never
// before.
//
// The alternative, where the database always wins, would have silently reset those deployments to the
// built-in values the moment this shipped.

namespace MotsSupplierPortal.Infrastructure.Configuration;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

public interface ISystemSettingReader
{
    Task<string> GetAsync(string key, CancellationToken ct);
}

public sealed class SystemSettingReader(AppDbContext db, IConfiguration configuration) : ISystemSettingReader
{
    private static readonly Dictionary<string, string> ConfigurationKeys = new(StringComparer.Ordinal)
    {
        [SystemSettings.ExpiringSoonWindowDays] = "Documents:ExpiringSoonWindowDays",
        [SystemSettings.RenewalReminderDays] = "Documents:RenewalReminderDays",
    };

    public async Task<string> GetAsync(string key, CancellationToken ct)
    {
        var definition = SystemSettings.Find(key)
            ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Not a known system setting.");

        var stored = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (stored is not null) return stored;

        if (ConfigurationKeys.TryGetValue(key, out var configurationKey))
        {
            if (definition.Kind is SettingKind.IntegerList)
            {
                var configured = configuration.GetSection(configurationKey).Get<int[]>();
                if (configured is { Length: > 0 })
                {
                    return string.Join(',', configured.Distinct());
                }
            }
            else if (configuration[configurationKey] is { Length: > 0 } single)
            {
                return single;
            }
        }

        return definition.DefaultValue;
    }
}

public static class SystemSettingReaderExtensions
{
    public static async Task<int> GetIntAsync(this ISystemSettingReader reader, string key, CancellationToken ct)
    {
        var raw = await reader.GetAsync(key, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.Parse(SystemSettings.Find(key)!.DefaultValue, CultureInfo.InvariantCulture);
    }

    public static async Task<int[]> GetIntListAsync(this ISystemSettingReader reader, string key, CancellationToken ct)
    {
        var raw = await reader.GetAsync(key, ct);
        var parsed = Parse(raw);
        return parsed.Length > 0 ? parsed : Parse(SystemSettings.Find(key)!.DefaultValue);

        static int[] Parse(string value) =>
        [
            .. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
                .Where(n => n is not null)
                .Select(n => n!.Value)
                .Distinct()
                .OrderByDescending(n => n),
        ];
    }
}
