using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Infrastructure.Admin;

/// <summary>
/// SCR-725's read.
///
/// <para><b>The limits come from the code that enforces them.</b> MaxUploadBytes and the allowed-type map
/// are FileTypeSniffer's own constants - the ones UploadDocumentHandler checks against and
/// DocumentEndpoints sizes its multipart limit from - so this screen cannot report a cap the upload path
/// is not applying. Restating them here as configuration would create exactly that gap.</para>
///
/// <para><b>Reachability is probed now, not read from a cached health snapshot.</b> An operator opening
/// this screen is asking whether uploads work at this moment, and /health/ready's last result is not that
/// question. Both probes are exception-shaped: any failure is "unreachable", because a partial answer
/// ("reachable but erroring") is not something an operator can act on differently.</para>
/// </summary>
public sealed class StorageSettingsHandler(
    AppDbContext db,
    MinioFileStorage fileStorage,
    IVirusScanner scanner,
    IConfiguration configuration) : IGetStorageSettingsHandler
{
    public async Task<StorageSettingsDto> HandleAsync(CancellationToken ct)
    {
        var objectStorageReachable = await ProbeAsync(() => fileStorage.PingAsync(ct));

        // An empty stream is a real scan request that carries nothing, and the OUTCOME is the probe - not
        // whether it threw. Found by stopping the ClamAV container and watching this report "reachable"
        // anyway: ClamAvScanner is fail-closed by design and swallows every failure into
        // ScanOutcome.Infected, so a connection refused and a real detection look identical to a caller
        // watching for exceptions. That fail-closed behaviour is right for scanning an upload and useless
        // as a liveness signal.
        //
        // So: Clean means the daemon answered. A working ClamAV cannot call an empty stream infected, which
        // makes anything other than Clean here "the scanner did not answer". Inferred from the scanner's
        // own contract and then confirmed both ways against a stopped and a running container.
        var scannerReachable = await ProbeScanAsync(scanner, ct);

        var documentCount = await db.SupplierDocuments.AsNoTracking().CountAsync(ct);
        var pendingScanCount = await db.SupplierDocuments.AsNoTracking()
            .CountAsync(d => d.State == DocumentState.PendingScan, ct);

        return new StorageSettingsDto(
            FileTypeSniffer.MaxSizeBytes,
            FileTypeSniffer.AllowedExtensionToContentType,
            configuration["Minio:Bucket"] ?? string.Empty,
            objectStorageReachable,
            scannerReachable,
            documentCount,
            pendingScanCount);
    }

    private static async Task<bool> ProbeScanAsync(IVirusScanner scanner, CancellationToken ct)
    {
        try
        {
            using var empty = new MemoryStream();
            return await scanner.ScanAsync(empty, ct) == ScanOutcome.Clean;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeAsync(Func<Task> probe)
    {
        try
        {
            await probe();
            return true;
        }
        catch
        {
            // Swallowed deliberately and reported as a boolean: the exception's text is a dependency's
            // internals, and putting it on an admin screen would leak infrastructure detail to answer a
            // yes-or-no question. The health endpoint already carries the diagnostic version.
            return false;
        }
    }
}
