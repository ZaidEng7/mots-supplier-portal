// The storage and scanning screen: the upload limits, the bucket, and how many documents are stored and waiting for
// the scanner.
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
// IT NO LONGER ASKS THE OBJECT STORE OR THE SCANNER
//
// It used to probe both every time the operations page opened, which made every visit to that page a call to two
// services outside the database whether or not anybody wanted the answer. Whether they answer is now a separate
// request, the storage probe beside the dashboard's system health section, made when somebody presses the button
// on the storage card. DependencyProbes holds how the two are asked and why a scanner that did not throw is not
// necessarily one that answered.

namespace MotsSupplierPortal.Infrastructure.Admin;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class StorageSettingsHandler(
    AppDbContext db,
    IConfiguration configuration) : IGetStorageSettingsHandler
{
    public async Task<StorageSettingsDto> HandleAsync(CancellationToken ct)
    {
        var documentCount = await db.SupplierDocuments.AsNoTracking().CountAsync(ct);
        var pendingScanCount = await db.SupplierDocuments.AsNoTracking()
            .CountAsync(d => d.State == DocumentState.PendingScan, ct);

        return new StorageSettingsDto(
            FileTypeSniffer.MaxSizeBytes,
            FileTypeSniffer.AllowedExtensionToContentType,
            configuration["Minio:Bucket"] ?? string.Empty,
            documentCount,
            pendingScanCount);
    }
}
