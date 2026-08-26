namespace MotsSupplierPortal.Infrastructure.Storage;

public sealed class ClamAvOptions
{
    public const string SectionName = "ClamAv";

    public required string Host { get; init; }
    public int Port { get; init; } = 3310;
}
