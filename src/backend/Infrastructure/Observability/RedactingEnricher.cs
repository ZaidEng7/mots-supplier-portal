// The central redaction stage on the log pipeline.
//
// Any log property whose NAME contains a deny-listed term has its value replaced before the event reaches any
// sink, so the guarantee holds for every call site including ones written later. It is not per-call-site
// discipline.
//
// This is the pipeline the audit difference builder mirrors. That type's own note referenced this stage before it
// existed, and in the meantime password-reset and verification tokens were reaching the logs in full.
//
// The deny-list is kept in step with the audit builder's, and both derive from the written security requirements.
//
//
// SCOPE AND LIMITS, STATED SO NOBODY ASSUMES MORE
//
// It matches property NAMES rather than values. A secret placed inside an innocuously named property is NOT
// caught. The structural defence for that is to not log the payload at all, which is what the logging email sender
// does by logging a template identifier instead of a rendered body.
//
// And it applies to log events only. Audit differences are redacted separately, when they are built, because they
// are persisted rather than logged.
//
// The properties are materialised before the loop, because adding one mutates the dictionary being iterated.

namespace MotsSupplierPortal.Infrastructure.Observability;

using Serilog.Core;
using Serilog.Events;

public sealed class RedactingEnricher : ILogEventEnricher
{
    public const string RedactedPlaceholder = "***REDACTED***";

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
        var sensitive = logEvent.Properties.Keys.Where(IsSensitiveName).ToArray();

        foreach (var name in sensitive)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(RedactedPlaceholder)));
        }
    }
}
