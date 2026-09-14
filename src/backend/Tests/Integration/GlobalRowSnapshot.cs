// Every globally shared row is what the seed made it, when the run ends.
//
//
// THE CLASS OF DEFECT
//
// One database is shared by a hundred and forty-five test classes, serialised but never reset.
//
// A test that changes a row nobody owns, a role's permissions, a feature switch, a reference row, a template
// override, and does not put it back has changed the world for everything that runs after it.
//
// Two of those have already been paid for. One suite overwrote a role's permissions and left them overwritten, so
// the governance suite passed alone and failed in a full run. Another grants a permission to a role it does not
// normally hold, and a third carries a comment explaining that its sweep had to be rewritten because of it.
//
//
// WHY A SNAPSHOT RATHER THAN A RULE PER TEST
//
// A per-test rule is the thing that was already being applied, file by file, as each new instance bit.
//
// This is the denominator. It does not care which test changed a row, only that the row is back. A leak
// introduced tomorrow fails the run and NAMES the row.
//
//
// ADDITIONS ARE NOT DRIFT
//
// Only rows the seed created are compared, by their own keys.
//
// A test that creates a reference code of its own, or an override for a key nobody else uses, changes nothing
// another test reads, and forbidding that would forbid most of the suite.
//
//
// WHAT IS IN THE SNAPSHOT, AND WHY EACH ENTRY
//
// Typed out by hand, the way every list in this repository is. Each entry is here because something outside its
// own test reads it: permissions decide what every persona may do, the field switches drive disclosure and
// re-review, the overrides are what a supplier actually receives, and the reference flags decide which documents
// suspend a supplier.
//
// The role permissions are read as raw projections rather than through an entity, because those claim rows belong
// to the identity framework and this context does not map them.
//
// For overrides it records the KEYS that carry one rather than their text. A test that edits the body of an
// override it created is its own business; one that leaves an override standing on a key the product ships
// without changes what every later test's email or screen says.
//
// The difference is rendered as a message a person can act on, with both sides serialised so it can be diffed by
// eye.

namespace MotsSupplierPortal.Tests.Integration;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class GlobalRowSnapshot
{
    internal static async Task<IReadOnlyDictionary<string, string>> TakeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);

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

    private sealed record RoleClaimRow(string RoleName, string ClaimValue);

    internal static string Describe(IReadOnlyDictionary<string, string> snapshot) =>
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
}
