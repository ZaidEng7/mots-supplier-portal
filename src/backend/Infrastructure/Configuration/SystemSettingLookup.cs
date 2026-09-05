using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Domain.Configuration;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Configuration;

/// <summary>
/// FR-ADM-006. Reads a setting for the code that consumes it.
///
/// <para><b>Precedence: stored row, then the deployment's configuration, then the definition's
/// default</b> (D-32). Two of these settings were reachable only through appsettings before this
/// table existed, and an environment that set them there did so on purpose; making the database win
/// only when a row EXISTS means the screen takes over a setting when an administrator touches it and
/// never before. The alternative - database always wins - would have silently reset those
/// deployments to 30/14/3 the moment this shipped.</para>
/// </summary>
public interface ISystemSettingReader
{
    Task<string> GetAsync(string key, CancellationToken ct);
}

public sealed class SystemSettingReader(AppDbContext db, IConfiguration configuration) : ISystemSettingReader
{
    /// <summary>Configuration keys for the settings that had one before this table. A setting absent
    /// from this map has no deployment-level spelling and falls straight through to its default.</summary>
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
            // The list form is a configuration ARRAY (Documents:RenewalReminderDays:0), not a
            // comma-separated string, which is how it was already written where it exists.
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

/// <summary>Typed readers, so a consumer never parses a setting itself and two consumers cannot
/// disagree about what "30,14,3" means.</summary>
public static class SystemSettingReaderExtensions
{
    public static async Task<int> GetIntAsync(this ISystemSettingReader reader, string key, CancellationToken ct)
    {
        var raw = await reader.GetAsync(key, ct);
        // The stored value passed the definition's validation on the way in, and the fallbacks are
        // the definition's own - but a hand-edited row or an appsettings typo must not take the job
        // down, so an unparseable value degrades to the default rather than throwing.
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
