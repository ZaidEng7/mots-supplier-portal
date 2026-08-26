namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// File storage abstraction (docs/security/SECURITY-ARCHITECTURE.md §4.1) - never a public bucket,
/// quarantine-first uploads, short-lived signed download URLs.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task MoveAsync(string sourceKey, string destinationKey, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Task<string> GetSignedDownloadUrlAsync(string key, TimeSpan expiry, string downloadFileName, CancellationToken ct);
}
