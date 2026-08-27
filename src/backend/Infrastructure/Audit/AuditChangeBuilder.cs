using System.Text.Json;
using MotsSupplierPortal.Infrastructure.Observability;

namespace MotsSupplierPortal.Infrastructure.Audit;

/// <summary>Builds the `changes` JSON diff for AuditLog (DATABASE-MODEL.md §5: "field-level
/// before/after (redacted for PII)"). Shares its deny-list with <see cref="RedactingEnricher"/>,
/// the log-pipeline redaction stage, by calling into it directly rather than keeping a parallel
/// copy - the two cannot drift apart (SECURITY-ARCHITECTURE.md: password, token, authorization,
/// secret, iban, otp).
///
/// Applied here by field-name substring match, as a backstop only. Fields the caller already
/// knows are sensitive (e.g. a bank account number) must be passed pre-masked rather than relying
/// on this deny-list, since it recognizes IBAN-shaped names but not e.g. "accountNumber".</summary>
internal static class AuditChangeBuilder
{
    /// <summary>Only fields whose before/after actually differ are included. Returns null (no
    /// `changes` column write) when nothing differs, so a no-op edit doesn't create a misleading
    /// empty-but-present diff.</summary>
    public static string? Build(params (string Field, object? Before, object? After)[] fields)
    {
        var diff = new Dictionary<string, object?>();
        foreach (var (field, before, after) in fields)
        {
            if (Equals(before, after)) continue;

            var isSensitive = RedactingEnricher.IsSensitiveName(field);
            var redacted = RedactingEnricher.RedactedPlaceholder;
            diff[field] = new { before = isSensitive ? redacted : before, after = isSensitive ? redacted : after };
        }

        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff);
    }
}
