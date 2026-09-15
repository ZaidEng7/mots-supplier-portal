// Refuses to start when a setting that has no safe default is missing, and warns about settings that
// are legal but combine into behaviour nobody asked for.
//
//
// WHY IT EXISTS
//
// Not one bug but a class of them. Three settings carried a hardcoded local fallback so that local
// development would work without configuration, and each degraded silently rather than failing when the
// real configuration was missing.
//
// The database connection quietly pointed at a local machine instead of the real database. The public
// address of the site quietly shipped local links in every verification, password-reset, resend and
// invitation email, so account recovery was dead with no error anywhere. The allowed browser origins
// quietly allowed only the local one, so the real interface was blocked.
//
// None of them logged, threw, or failed a health check. A misconfigured deployment looked healthy and
// was not. Checking here means it fails at start-up, where somebody is watching, rather than months
// later in a user's inbox.
//
// Local development is deliberately exempt, because its own settings file supplies all of these, and
// requiring them would add friction to running the project without protecting anything.
//
//
// THE REQUIRED SETTINGS
//
// Add to that list rather than reintroducing an inline local fallback.
//
// The signing settings already had their own refusal further down the start-up sequence, but that one
// fires after this check, so a deployment missing both learned about them one redeploy apart, which is
// the exact failure this class exists to prevent. They are listed here so the start-up error reports
// everything at once, and the deeper structural check still stands.
//
// THE TWO KEYS ARE HERE FOR THE SAME REASON THE CONNECTION STRING IS, and they are the sharpest case
// of it. Both generate an ephemeral value when none is configured, which is right for local development
// and silent ruin in production.
//
// A missing Jwt:RsaPrivateKeyPem gives every replica its own signing key, so a token minted by one is
// refused by the next, and a restart signs everybody out. It reads as an intermittent 401 and gets
// blamed on the load balancer.
//
// A missing FieldEncryption:DataKeyBase64 is worse, because it destroys data rather than sessions. Bank
// account numbers are encrypted with it, and a key that does not outlive the process makes every number
// written under it unreadable the moment that process ends - unrecoverably, and with no error at the
// time of writing. The failure surfaces the first time somebody opens a supplier's banking details,
// which may be long after the deployment that caused it.
//
// Neither had a refusal anywhere, which is precisely the shape of the three settings in the paragraph
// above. Local development is still exempt, because its own settings file supplies neither and the
// ephemeral fallback is what makes a fresh checkout run.
//
// The mail host and sender address are declared as required where they are bound, but that only fires
// the moment something actually resolves them, which is the first real email send and could be hours
// after a bad deployment. They are listed here so a missing mail section is caught at start-up. The
// user name and password are deliberately not required, because an internal relay that permits
// anonymous sending has no credential to supply.
//
// Every missing key is reported at once. Discovering them one redeploy at a time is its own small
// outage.
//
// The allowed origins need their own check, because a list binds as numbered children, so testing the
// parent key for emptiness cannot tell "absent" from "present but empty".
//
//
// THE WARNINGS
//
// These are returned rather than thrown. They describe configurations that are suboptimal rather than
// broken, and refusing to start over a questionable-but-working setup would be a worse failure than the
// thing it prevents.
//
// They exist because documenting an interaction on the settings involved is not sufficient, and this
// project has the evidence. A comment explaining a constraint does not survive contact with somebody
// changing the value, because the person changing it is looking at a configuration file rather than at
// the code. A setting that looks free and has a real interaction is the same trap as a comment that has
// rotted into a lie, milder but the same shape. Saying it at start-up puts the warning in front of the
// person who caused it, at the moment they caused it.
//
// They run in every environment, local development included, because that is where somebody experiments
// with a value before promoting it.
//
// The first warning is about a document expiry window wider than the widest reminder in the ladder,
// which leaves a document sitting in the expiring state for days before its supplier is told anything.
// The ladder is deliberately not widened to follow the window, because a reminder schedule should be a
// list of decisions rather than a side effect of a threshold, so the warning tells the reader to add a
// rung if that silence is unwanted.
//
// The second is about scheduled work being switched off. That is a legitimate setting for a deployment
// that runs no background worker, which is why it is a warning rather than a refusal, and almost
// certainly not intended anywhere else. It is stated in terms of consequence rather than mechanism: the
// person reading it at start-up may not know what a recurring job is in this system, but they do know
// what a tender is.

namespace MotsSupplierPortal.Api.Configuration;

public static class RequiredConfiguration
{
    private static readonly string[] RequiredKeys =
    [
        "ConnectionStrings:Default",
        "App:PublicUrl",
        "Jwt:Issuer",
        "Jwt:Audience",
        "Jwt:RsaPrivateKeyPem",
        "FieldEncryption:DataKeyBase64",
        "Smtp:Host",
        "Smtp:FromAddress",
    ];

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var missing = RequiredKeys
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();

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

        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        var recurringEnabled = configuration.GetValue("Jobs:EnableRecurring", true);

        if (!recurringEnabled && !string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Jobs:EnableRecurring is false, so no scheduled work will run in this environment. " +
                "RFQ submission windows will never open and never close, document expiry will never " +
                "be flagged, awards will never reconcile to the ERP, and queued outbox messages will " +
                "never be dispatched - all silently, with no error anywhere. This is a legitimate " +
                "setting for a deployment that runs no background worker; if this is not that, " +
                "remove the setting or set it to true.");
        }

        return warnings;
    }
}
