namespace MotsSupplierPortal.Application.Common;

public enum ScanOutcome
{
    Clean,
    Infected,
}

public interface IVirusScanner
{
    Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct);
}
