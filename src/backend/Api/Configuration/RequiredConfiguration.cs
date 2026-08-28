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
}
