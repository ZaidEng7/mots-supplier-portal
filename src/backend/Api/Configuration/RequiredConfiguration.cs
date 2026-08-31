namespace MotsSupplierPortal.Api.Configuration;

/// <summary>
/// Fail-fast validation of settings that must be present outside Development.
///
/// This exists because of a defect *class*, not a single bug. Three separate settings carried a
/// literal localhost fallback so local development would work without configuration, and each one
/// degraded silently rather than failing when that configuration was missing in a real
/// environment:
///
/// - ConnectionStrings:Default  -> quietly pointed at localhost instead of the real database.
/// - App:PublicUrl              -> quietly shipped "http://localhost:5173" links in every
///                                 verification, password-reset, resend and invite email, so
///                                 account recovery was dead with no error anywhere.
/// - Cors:AllowedOrigins        -> quietly allowed only localhost, so the real SPA was blocked.
///
/// None of them logged, threw, or failed a health check. A misconfigured deployment looked healthy
/// and was not. Validating here means it fails at boot, where somebody is watching, instead of
/// months later in a user's inbox.
///
/// Development is deliberately exempt: appsettings.Development.json supplies all of these, and
/// requiring them would only add friction to `dotnet run` without protecting anything.
/// </summary>
public static class RequiredConfiguration
{
    /// <summary>Settings with no safe default outside Development. Add to this list rather than
    /// reintroducing an inline `?? "http://localhost..."` fallback.</summary>
    private static readonly string[] RequiredKeys =
    [
        "ConnectionStrings:Default",
        "App:PublicUrl",
        // Jwt already had its own throw further down Program.cs, but that fires *after* this
        // check - so a deployment missing both learned about them one redeploy apart, which is
        // the exact failure mode this class exists to prevent. Listed here so the boot error
        // reports everything at once. The structural validation downstream still stands.
        "Jwt:Issuer",
        "Jwt:Audience",
        // Task #35: SmtpOptions.Host/FromAddress are `required` (binding-time failure), but that
        // only fires the moment something actually resolves IOptions<SmtpOptions>.Value - the
        // first real email send, which could be hours after a bad deploy. Listed here so a missing
        // Smtp section is caught at boot instead. User/Password are deliberately NOT required: an
        // internal relay or a permitted-anonymous-relay setup has no credential to supply.
        "Smtp:Host",
        "Smtp:FromAddress",
    ];

    /// <summary>Throws when a required setting is absent in a non-Development environment.
    /// Reports every missing key at once - discovering them one redeploy at a time is its own
    /// small outage.</summary>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var missing = RequiredKeys
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();

        // Array-valued settings bind as indexed children, so a null check on the parent key is not
        // enough to tell "absent" from "present but empty".
        if (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is not { Length: > 0 })
        {
            missing.Add("Cors:AllowedOrigins");
        }

        if (missing.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Required configuration missing for environment '{environment.EnvironmentName}': " +
            $"{string.Join(", ", missing)}. " +
            "Supply these from the environment or a secret store. The application will not start " +
            "with defaults, because the previous defaults failed silently in production.");
    }

    /// <summary>
    /// Settings that are legal, but whose combination produces behaviour nobody asked for. Returned
    /// rather than thrown: these are suboptimal, not broken, and refusing to boot over a
    /// questionable-but-working configuration would be a worse failure than the thing it prevents.
    ///
    /// <para><b>Why this exists at all.</b> The interaction below is accurately documented on both
    /// settings it involves. That is not sufficient, and this project has the evidence: a comment
    /// explaining a constraint does not survive contact with someone changing the value, because the
    /// person changing it is looking at a config file rather than at the code. A setting that looks
    /// free and has a real interaction is the same trap as a comment that has rotted into a lie -
    /// milder, but the same shape. Saying it at boot puts the warning in front of the person who
    /// caused it, at the moment they caused it.</para>
    ///
    /// <para>Runs in every environment, Development included - that is where someone experiments
    /// with a value before promoting it.</para>
    /// </summary>
    public static IReadOnlyList<string> Warnings(IConfiguration configuration)
    {
        var warnings = new List<string>();

        var window = configuration.GetValue("Documents:ExpiringSoonWindowDays", 30);
        var cadence = configuration.GetSection("Documents:RenewalReminderDays").Get<int[]>() is { Length: > 0 } configured
            ? configured
            : [30, 14, 3];

        var widestRung = cadence.Max();

        if (window > widestRung)
        {
            warnings.Add(
                $"Documents:ExpiringSoonWindowDays is {window} but the widest renewal reminder rung " +
                $"is {widestRung} (BRULE-021 vs BRULE-025). A document will sit in ExpiringSoon for " +
                $"{window - widestRung} days before its supplier is told anything. The reminder " +
                "ladder is deliberately NOT widened to follow this setting - a reminder schedule " +
                "should be a list of decisions, not a side effect of a threshold - so if that " +
                $"silence is unwanted, add a {window}-day rung to Documents:RenewalReminderDays.");
        }

        return warnings;
    }
}
