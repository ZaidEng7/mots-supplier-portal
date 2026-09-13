// Scanning one uploaded file, and the three answers a scan can give: clean, infected, or the scanner could
// not be reached.
//
// The third is its own outcome rather than an exception, because a scanner being down is an operational
// state the upload pipeline has to decide about, not a programming error.

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
