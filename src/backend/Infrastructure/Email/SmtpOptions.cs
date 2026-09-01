namespace MotsSupplierPortal.Infrastructure.Email;

/// <summary>
/// Task #35. Host and FromAddress are `required` so binding fails the moment the section is
/// missing or malformed - same shape as MinioOptions/ClamAvOptions. User/Password are left
/// nullable rather than required: a local dev catcher (MailHog) and some internal relays accept
/// unauthenticated mail, so forcing credentials here would make anonymous SMTP impossible to
/// configure. RequiredConfiguration.RequiredKeys covers Host/FromAddress for non-Development
/// boots; auth is opt-in per environment via whether User/Password are present.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public required string Host { get; init; }
    public int Port { get; init; } = 25;
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string FromAddress { get; init; }
    public bool UseSsl { get; init; }
}
