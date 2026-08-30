using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Storage;

/// <summary>
/// S3-compatible file storage against the MinIO container (docs/architecture foundational §2's
/// IFileStorage; local dev and prod both point at an S3-compatible endpoint rather than a
/// separate local-disk implementation, since MinIO is already the provisioned dev storage).
///
/// Uses AWSSDK.S3 rather than the official MinIO .NET SDK: the MinIO SDK (both 6.0.4 and 7.0.0)
/// was tried first and silently "succeeds" on PutObject against this MinIO server version
/// (empty ETag, and StatObject afterward shows 0 bytes - the object never actually lands).
/// AWSSDK.S3 round-trips correctly (real ETag, verified via a genuine HEAD request) and is the
/// far more widely-used, mature client for talking to S3-compatible stores including MinIO.
/// </summary>
public sealed class MinioFileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _useSsl;

    public MinioFileStorage(IOptions<MinioOptions> options)
    {
        var opts = options.Value;
        _bucket = opts.Bucket;
        _useSsl = opts.UseSsl;
        _client = new AmazonS3Client(opts.AccessKey, opts.SecretKey, new AmazonS3Config
        {
            ServiceURL = $"{(opts.UseSsl ? "https" : "http")}://{opts.Endpoint}",
            ForcePathStyle = true,
            UseHttp = !opts.UseSsl,
        });
    }

    public async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        try
        {
            await _client.GetBucketLocationAsync(_bucket, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _client.PutBucketAsync(_bucket, ct);
        }
    }

    /// <summary>Task #16: read-only reachability probe for the readiness health check
    /// (ObjectStorageHealthCheck) - deliberately NOT EnsureBucketExistsAsync, which mutates
    /// (creates the bucket) on a 404. A readiness probe an orchestrator polls every few seconds
    /// must never have a side effect; it answers "is the endpoint reachable and responding",
    /// nothing more.</summary>
    public async Task PingAsync(CancellationToken ct) => await _client.GetBucketLocationAsync(_bucket, ct);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, ct);
    }

    // MSP-84/NFR-PERF-008: the only caller is DocumentScanJob, which already streams into ClamAV
    // in 8KB chunks (ClamAvScanner.cs) - copying the whole object into a MemoryStream here first
    // defeated that entirely, holding the full file in managed heap before ever handing it to a
    // scanner that never needed more than 8KB at a time. GetObjectResponse.ResponseStream is
    // itself a live network stream over the S3/MinIO connection; returning it directly disposes
    // the response correctly (AWS SDK wires stream disposal to the response) and never
    // materializes the file in memory at all on this path.
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task MoveAsync(string sourceKey, string destinationKey, CancellationToken ct)
    {
        await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _bucket,
            SourceKey = sourceKey,
            DestinationBucket = _bucket,
            DestinationKey = destinationKey,
        }, ct);
        await DeleteAsync(sourceKey, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await _client.DeleteObjectAsync(_bucket, key, ct);
    }

    public Task<string> GetSignedDownloadUrlAsync(string key, TimeSpan expiry, string downloadFileName, CancellationToken ct)
    {
        // attachment disposition (docs/security §4.1): never served inline from the app origin.
        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _useSsl ? Protocol.HTTPS : Protocol.HTTP,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{downloadFileName}\"",
            },
        });
        return Task.FromResult(url);
    }
}
