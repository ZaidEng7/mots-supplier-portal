// The storage and scanning screen: the upload limits, and whether the two dependencies are answering.
//
//
// THE LIMITS COME FROM THE CODE THAT ENFORCES THEM
//
// The maximum size and the allowed-type map are the sniffer's own constants, which are what the upload path
// checks against and what the endpoint sizes its request limit from.
//
// So this screen cannot report a cap the upload path is not applying. Restating them here as configuration would
// create exactly that gap.
//
//
// REACHABILITY IS PROBED NOW, NOT READ FROM A CACHED SNAPSHOT
//
// An operator opening this screen is asking whether uploads work at this moment, and the readiness endpoint's
// last result is not that question.
//
// Any failure is reported as unreachable, because a partial answer, reachable but erroring, is not something an
// operator can act on differently. The exception text is swallowed deliberately: it is a dependency's internals,
// and putting it on an administration screen would leak infrastructure detail to answer a yes-or-no question.
// The health endpoint already carries the diagnostic version.
//
//
// THE SCANNER PROBE IS ABOUT THE OUTCOME, NOT ABOUT WHETHER IT THREW
//
// Found by stopping the scanner and watching this report "reachable" anyway. The scanner is fail-closed by
// design and swallows every failure into an infected verdict, so a refused connection and a real detection look
// identical to a caller watching for exceptions.
//
// That fail-closed behaviour is right for scanning an upload and useless as a liveness signal.
//
// So: a clean verdict means the daemon answered. A working scanner cannot call an empty stream infected, which
// makes anything else here "the scanner did not answer". Inferred from the scanner's own contract and then
// confirmed both ways against a stopped and a running container.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class StorageSettingsHandler(
    AppDbContext db,
    MinioFileStorage fileStorage,
    IVirusScanner scanner,
    IConfiguration configuration) : IGetStorageSettingsHandler
{
    public async Task<StorageSettingsDto> HandleAsync(CancellationToken ct)
    {
        var objectStorageReachable = await ProbeAsync(() => fileStorage.PingAsync(ct));

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
            return false;
        }
    }
}
