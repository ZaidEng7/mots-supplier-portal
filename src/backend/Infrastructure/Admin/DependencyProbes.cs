// Asking the object store and the virus scanner whether they answer, within a time limit, as a plain yes or no.
//
// Shared by the dashboard's system health section, which pings the object store on every read, and by the
// on-demand storage probe, which asks both. The operations page's storage settings used to probe both every time
// the page opened; they now report the settings only, and the page asks for a probe when somebody presses its
// button.
//
//
// THE OBJECT STORE IS ASKED THROUGH ITS READINESS CHECK
//
// The readiness endpoint already has a check for the object store, built on the same client the uploads use and
// deliberately read-only. Running that one check by name, rather than calling the client again here, means the
// dashboard and the readiness endpoint cannot disagree about what "reachable" means.
//
// A check that did not run is not a healthy one. Asking for a check by a name nobody registered gives an empty,
// healthy report, so the answer is yes only when the report has an entry under that name and the entry is
// healthy.
//
//
// THE SCANNER PROBE IS ABOUT THE OUTCOME, NOT ABOUT WHETHER IT THREW
//
// Found by stopping the scanner and watching the old storage screen report "reachable" anyway. The scanner does
// not throw when the daemon is down: it reports the scan as unavailable (it used to report it as infected), so a
// caller watching for exceptions sees nothing wrong.
//
// So: a clean verdict on an empty stream means the daemon answered. A working scanner cannot call an empty stream
// infected, which makes anything else here - unavailable, or an infected verdict nobody could explain - "the
// scanner did not answer". Inferred from the scanner's own contract and then confirmed both ways against a stopped
// and a running container.
//
//
// THE LIMIT HOLDS EVEN AGAINST A CALL THAT IGNORES CANCELLATION
//
// The call is handed a token that is cancelled at the limit, and the wait for it also stops at the limit on its
// own. A dependency that ignores its token is then left to finish in the background while the answer goes back as
// no, rather than holding the screen for as long as the dependency's own timeout.
//
// Any failure is no. The exception text is swallowed deliberately: it is a dependency's internals, and an
// administration screen asking a yes-or-no question is not the place for them. The readiness endpoint and the
// server log carry the diagnostic version. Cancellation of the request itself is the exception, and is allowed to
// propagate, because the caller has gone.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Application.Common;

public static class DependencyProbes
{
    public const string ObjectStorageCheck = "object-storage";

    public static Task<bool> ObjectStorageAnswersAsync(
        HealthCheckService healthChecks, TimeSpan limit, CancellationToken ct) =>
        WithinAsync(
            async token =>
            {
                var report = await healthChecks.CheckHealthAsync(
                    registration => registration.Name == ObjectStorageCheck, token);

                return report.Entries.TryGetValue(ObjectStorageCheck, out var entry)
                       && entry.Status == HealthStatus.Healthy;
            },
            limit,
            ct);

    public static Task<bool> ScannerAnswersAsync(IVirusScanner scanner, TimeSpan limit, CancellationToken ct) =>
        WithinAsync(
            async token =>
            {
                using var empty = new MemoryStream();
                return await scanner.ScanAsync(empty, token) == ScanOutcome.Clean;
            },
            limit,
            ct);

    private static async Task<bool> WithinAsync(
        Func<CancellationToken, Task<bool>> probe, TimeSpan limit, CancellationToken ct)
    {
        using var cancelAtLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancelAtLimit.CancelAfter(limit);

        try
        {
            return await probe(cancelAtLimit.Token).WaitAsync(limit, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await cancelAtLimit.CancelAsync();
            return false;
        }
    }
}
