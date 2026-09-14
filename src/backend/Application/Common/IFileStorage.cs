// Storing and serving files.
//
// Never a public bucket. Uploads land in quarantine first, and downloads are served through short-lived
// signed links rather than by handing out a path.

namespace MotsSupplierPortal.Application.Common;

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task MoveAsync(string sourceKey, string destinationKey, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Task<string> GetSignedDownloadUrlAsync(string key, TimeSpan expiry, string downloadFileName, CancellationToken ct);
}
