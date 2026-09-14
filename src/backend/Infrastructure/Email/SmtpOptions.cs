// The mail transport's configuration.
//
// The host and the sending address are required, so binding fails the moment the section is missing or
// malformed, which is the same shape the other infrastructure options have.
//
// The credentials are deliberately optional rather than required. A local development catcher and some internal
// relays accept unauthenticated mail, so forcing credentials here would make anonymous sending impossible to
// configure.
//
// The startup configuration check covers the host and the address for non-development boots; authentication is
// opt-in per environment by whether the credentials are present.

namespace MotsSupplierPortal.Infrastructure.Email;

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
