using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-073: every globally shared row is what the seed made it, when the run ends.
///
/// <para><b>The class of defect.</b> One database is shared by 145 test classes, serialized but never
/// reset. A test that changes a row nobody owns - a role's permissions, a feature flag, a reference
/// row, a template override - and does not put it back has changed the world for everything that runs
/// after it. Two of those have already been paid for: <c>ManageRolesTests</c> overwrote
/// <c>ministry_viewer</c> and left it overwritten, so the governance suite passed alone and failed in
/// a full run; <c>ReportEndpointsTests</c> grants <c>report.read</c> to <c>procurement_officer</c>,
/// and <c>AuthorizationFuzzTests</c> carries a comment explaining that its sweep had to be rewritten
/// because of it.</para>
///
/// <para><b>Why a snapshot rather than a rule per test.</b> The entry that asked for this asked for a
/// systematic pass, and a per-test rule is the thing that was already being applied - file by file, as
/// each new instance bit. This is the denominator: it does not care which test changed a row, only
/// that the row is back. A leak introduced tomorrow fails the run and NAMES the row.</para>
///
/// <para><b>Additions are not drift.</b> Only rows the seed created are compared, by their own keys.
/// A test that creates a reference code of its own, or a new override for a key nobody else uses,
/// changes nothing another test reads - and forbidding that would forbid most of the suite.</para>
/// </summary>
internal static class GlobalRowSnapshot
{
    /// <summary>
    /// The shared rows, as a dictionary of key to value, read straight from the database.
    ///
    /// <para>Typed out by hand, the way every list in this repository is. Each entry is here because
    /// something outside its own test reads it: permissions decide what every persona may do, the
    /// field config drives disclosure and re-review, the overrides are what a supplier actually
    /// receives, and the reference flags decide which documents suspend a supplier.</para>
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, string>> TakeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);

        // Role permissions. The claim rows are identity's, so they are read as raw SQL projections
        // rather than through an entity this context does not map.
        var claims = await db.Database.SqlQuery<RoleClaimRow>($"""
            SELECT r."Name" AS "RoleName", c."ClaimValue" AS "ClaimValue"
            FROM identity.role r
            JOIN identity.role_claim c ON c."RoleId" = r."Id"
            WHERE c."ClaimType" = 'perms'
            """).ToListAsync(ct);

        foreach (var group in claims.GroupBy(c => c.RoleName))
        {
            rows[$"role:{group.Key}"] = string.Join(",", group.Select(c => c.ClaimValue).OrderBy(v => v, StringComparer.Ordinal));
        }

        foreach (var setting in await db.SystemSettings.AsNoTracking().ToListAsync(ct))
        {
            rows[$"setting:{setting.Key}"] = setting.Value;
        }

        foreach (var config in await db.SupplierFieldConfigs.AsNoTracking().ToListAsync(ct))
        {
            rows[$"fieldConfig:{config.Category}/{config.FieldCode}"] = config.IsEnabled.ToString();
        }

        foreach (var type in await db.DocumentTypes.AsNoTracking().ToListAsync(ct))
        {
            rows[$"documentType:{type.Code}"] = $"active={type.IsActive},required={type.IsRequired},expiry={type.ExpiryTracked},awardCritical={type.IsAwardCritical}";
        }

        // Overrides: the KEYS that carry one, not their text. A test that edits the body of an
        // override it created is its own business; one that leaves an override standing on a key the
        // product ships without changes what every later test's email or screen says.
        rows["emailOverrides"] = string.Join(",",
            (await db.EmailTemplateOverrides.AsNoTracking().Select(o => o.Key).ToListAsync(ct))
            .OrderBy(k => k, StringComparer.Ordinal));

        rows["notificationTemplates"] = string.Join(",",
            (await db.NotificationTemplates.AsNoTracking().Select(t => t.Type).ToListAsync(ct))
            .OrderBy(k => k, StringComparer.Ordinal));

        rows["uiStringOverrides"] = string.Join(",",
            (await db.UiStringOverrides.AsNoTracking().Select(o => o.Language + ":" + o.Key).ToListAsync(ct))
            .OrderBy(k => k, StringComparer.Ordinal));

        rows["documentTypeCategoryLinks"] = string.Join(",",
            (await db.DocumentTypeCategories.AsNoTracking().Select(l => l.DocumentTypeId.ToString() + "/" + l.CategoryCode).ToListAsync(ct))
            .OrderBy(k => k, StringComparer.Ordinal));

        return rows;
    }

    /// <summary>
    /// What changed between two snapshots, as a message a person can act on, or null when nothing did.
    /// </summary>
    internal static string? Drift(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        var lines = new List<string>();

        foreach (var (key, value) in before)
        {
            if (!after.TryGetValue(key, out var now))
            {
                lines.Add($"  {key}\n    was: {value}\n    now: <the row is gone>");
            }
            else if (now != value)
            {
                lines.Add($"  {key}\n    was: {value}\n    now: {now}");
            }
        }

        if (lines.Count == 0) return null;

        return "A test changed a globally shared row and did not put it back. Every test that ran\n"
             + "afterwards saw the changed value, and a suite that passes in one order can fail in\n"
             + "another (T-073). Wrap the mutation and its restore in try/finally so the restore\n"
             + "survives a failing assertion.\n\n"
             + string.Join("\n", lines);
    }

    /// <summary>The projection shape for the identity claim query.</summary>
    private sealed record RoleClaimRow(string RoleName, string ClaimValue);

    /// <summary>Serialised for the failure message, so a difference can be diffed by eye.</summary>
    internal static string Describe(IReadOnlyDictionary<string, string> snapshot) =>
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
}
