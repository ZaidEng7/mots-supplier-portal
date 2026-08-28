using Serilog.Core;
using Serilog.Events;

namespace MotsSupplierPortal.Infrastructure.Observability;

/// <summary>
/// NFR-PRIV-004 / BRULE-091: central redaction stage on the log pipeline. Any log property whose
/// NAME contains a deny-listed term has its value replaced before the event reaches any sink, so
/// the guarantee holds for every call site including ones written later - it is not per-call-site
/// discipline.
///
/// This is the pipeline <see cref="Audit.AuditChangeBuilder"/> mirrors. Before 2026-08-28 that
/// type's comment referenced this stage but it did not exist: password-reset and email-verification
/// tokens were reaching the logs in full (MSP-61).
///
/// Scope and limits, stated plainly so nobody assumes more than it does:
/// - Matches property NAMES, not values. A secret placed inside an innocuously-named property is
///   NOT caught. The structural defence for that is to not log the payload at all - see
///   <see cref="Email.LoggingEmailSender"/>, which logs a template id instead of a rendered body.
/// - Applies to log events only. Audit `changes` diffs are redacted separately, at build time, by
///   AuditChangeBuilder, because they are persisted rather than logged.
/// </summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    public const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>Kept in sync with AuditChangeBuilder.SensitiveFieldFragments - both derive from
    /// docs/security/SECURITY-ARCHITECTURE.md's redaction requirements.</summary>
    private static readonly string[] SensitiveNameFragments =
    [
        "password",
        "token",
        "authorization",
        "secret",
        "iban",
        "otp",
    ];

    public static bool IsSensitiveName(string propertyName) =>
        SensitiveNameFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Materialize first: AddOrUpdateProperty mutates the dictionary being iterated.
        var sensitive = logEvent.Properties.Keys.Where(IsSensitiveName).ToArray();

        foreach (var name in sensitive)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(RedactedPlaceholder)));
        }
    }
}
