// Where the ERP is, which credential opens it, and what time its clock is telling.
//
// THE TIME ZONE IS A REQUIRED SETTING BECAUSE THE ERP'S TIMESTAMPS HAVE NO ZONE. It sends
// "2026-08-17 03:15:00" and means its own server's local time. There is no offset to read and nothing in the
// payload to infer one from, so the zone has to be configured, and configuring it wrong is silent: every
// created-on and last-modified in the portal drifts by the difference, and the ministry's feed reports those
// drifted timestamps as if they were facts.
//
// IT DEFAULTS TO DAMASCUS rather than to UTC. A default of UTC would be the safe-looking choice and the wrong
// one - it is exactly the misreading this setting exists to prevent, and it would look correct in every log.
// Defaulting to the zone the ERP is actually in means the common case needs no configuration and the unusual
// case is a deliberate override.
//
// THE SECRET IS NOT IN THIS REPOSITORY and must not be. It belongs in user-secrets locally and in the
// deployment's configuration store beyond that. The sandbox key travelled over plain HTTP in a Word document,
// which is reason enough to treat every ERP credential as already compromised and rotate it.
//
// ENABLED EXISTS SO THE TEST SUITE STAYS HERMETIC. Nothing here should reach out during a test run, and the
// supplier import is not a control whose absence breaks anything, so switching it off is a legitimate
// configuration rather than a failure.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpOptions
{
    public const string SectionName = "Erp";

    public const string DefaultTimeZone = "Asia/Damascus";

    public bool Enabled { get; init; }
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string ApiSecret { get; init; }
    public required string Company { get; init; }
    public string ServerTimeZone { get; init; } = DefaultTimeZone;
}
