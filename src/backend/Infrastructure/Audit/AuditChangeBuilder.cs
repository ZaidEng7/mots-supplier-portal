using System.Text.Json;

namespace MotsSupplierPortal.Infrastructure.Audit;

/// <summary>Builds the `changes` JSON diff for AuditLog (DATABASE-MODEL.md §5: "field-level
/// before/after (redacted for PII)"). Redaction deny-list mirrors the Serilog redaction pipeline
/// (SECURITY-ARCHITECTURE.md: password, token, authorization, secret, iban, otp) - applied by
/// field-name substring match as a backstop. Fields the caller already knows are sensitive (e.g.
/// a bank account number) should be passed pre-masked rather than relying on this deny-list,
/// since the deny-list only recognizes IBAN-shaped names, not e.g. "accountNumber".</summary>
internal static class AuditChangeBuilder
{
    private const string Redacted = "***REDACTED***";

    private static readonly string[] SensitiveFieldNameSubstrings =
        ["password", "token", "authorization", "secret", "iban", "otp"];

    /// <summary>Only fields whose before/after actually differ are included. Returns null (no
    /// `changes` column write) when nothing differs, so a no-op edit doesn't create a misleading
    /// empty-but-present diff.</summary>
    public static string? Build(params (string Field, object? Before, object? After)[] fields)
    {
        var diff = new Dictionary<string, object?>();
        foreach (var (field, before, after) in fields)
        {
            if (Equals(before, after)) continue;

            var isSensitive = SensitiveFieldNameSubstrings.Any(s => field.Contains(s, StringComparison.OrdinalIgnoreCase));
            diff[field] = new { before = isSensitive ? Redacted : before, after = isSensitive ? Redacted : after };
        }

        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff);
    }
}
