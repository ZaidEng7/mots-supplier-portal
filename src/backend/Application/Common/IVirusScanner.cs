// Scanning one uploaded file, and the three answers a scan can give: clean, infected, or the scanner could
// not give an answer.
//
// The third is its own outcome rather than an exception, because a scanner being down is an operational
// state the upload pipeline has to decide about, not a programming error.
//
// UNAVAILABLE IS NOT INFECTED. It used to be: any error at all - a refused connection, a timeout, a reply the
// client did not recognise - came back as infected, and both callers delete an infected file. So a scanner outage
// did not stop anything unscanned getting through, which quarantine already guarantees; it destroyed every
// supplier document and tender attachment that happened to be scanned while the scanner was down. A file the
// scanner could not judge is held where it is and tried again: never released, never deleted.

namespace MotsSupplierPortal.Application.Common;

public enum ScanOutcome
{
    Clean,
    Infected,
    Unavailable,
}

public interface IVirusScanner
{
    Task<ScanOutcome> ScanAsync(Stream content, CancellationToken ct);
}

public sealed class VirusScannerUnavailableException()
    : Exception("The virus scanner could not give an answer; the file stays in quarantine and will be tried again.");
