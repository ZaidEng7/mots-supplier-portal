// The port behind the supplier registry export.
//
// It STREAMS rather than returning a list. The export is the whole registry with every child collection
// attached, and materialising all of it before the first byte reaches the client would hold the lot in memory
// and time out on a large enough registry. Streaming keeps memory flat and starts the download immediately,
// which is the same shape the audit trail export already uses.
//
// The lookups come back once, ahead of the rows, because the category, region and document-type names are the
// same for every supplier and reading them per row would be thousands of repeats of four small tables.

namespace MotsSupplierPortal.Application.Suppliers;

public interface ISupplierRegistryExportHandler
{
    Task<SupplierExportLookups> GetLookupsAsync(CancellationToken ct);

    IAsyncEnumerable<SupplierExportRecord> StreamAsync(CancellationToken ct);
}
