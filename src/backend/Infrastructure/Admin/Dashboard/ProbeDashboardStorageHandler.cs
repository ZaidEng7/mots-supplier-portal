// Asks the object store and the virus scanner whether they answer, when somebody presses the button that asks.
//
// The two are asked at the same time, each through DependencyProbes and each given ten seconds, so the whole probe
// answers within ten seconds however either of them misbehaves. The object store is asked through its readiness
// check, the scanner with an empty stream that a working scanner calls clean.
//
// It reads no rows, and it contacts nothing but those two services. The ERP is not one of them.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Application.Common;

public sealed class ProbeDashboardStorageHandler(HealthCheckService healthChecks, IVirusScanner scanner)
    : IProbeDashboardStorageHandler
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public async Task<DashboardStorageProbeDto> HandleAsync(CancellationToken ct)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        var objectStorage = DependencyProbes.ObjectStorageAnswersAsync(healthChecks, Limit, ct);
        var scannerAnswers = DependencyProbes.ScannerAnswersAsync(scanner, Limit, ct);

        return new DashboardStorageProbeDto(await objectStorage, await scannerAnswers, checkedAt);
    }
}
