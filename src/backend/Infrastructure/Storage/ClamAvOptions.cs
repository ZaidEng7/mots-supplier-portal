// The scanner's address.
//
// The host is required, so binding fails the moment the section is missing. The port defaults to the daemon's
// standard one.

namespace MotsSupplierPortal.Infrastructure.Storage;

public sealed class ClamAvOptions
{
    public const string SectionName = "ClamAv";

    public required string Host { get; init; }
    public int Port { get; init; } = 3310;
}
