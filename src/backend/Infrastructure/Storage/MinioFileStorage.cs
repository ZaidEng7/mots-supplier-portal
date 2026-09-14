// File storage against the object store.
//
// Local development and production both point at an object store rather than a separate local-disk
// implementation, because the store is already the provisioned development storage.
//
//
// IT USES THE GENERAL S3 CLIENT RATHER THAN THE STORE'S OWN
//
// The store's own client was tried first, in two versions, and silently "succeeded" on an upload against this
// server version: an empty checksum, and a subsequent metadata read showing zero bytes. The object never landed.
//
// The general client round-trips correctly, verified with a real metadata request, and is the far more widely used
// and mature client for talking to stores of this kind, including this one.
//
//
// THE READINESS PROBE IS READ-ONLY
//
// Deliberately not the bucket-ensuring call, which CREATES the bucket when it is absent.
//
// A readiness probe an orchestrator polls every few seconds must never have a side effect. It answers whether the
// endpoint is reachable and responding, and nothing more.
//
//
// A READ RETURNS THE LIVE NETWORK STREAM
//
// Its only caller already streams into the scanner in small chunks, and copying the whole object into memory here
// first defeated that entirely, holding the full file on the heap before handing it to a scanner that never needed
// more than a few kilobytes at a time.
//
// Returning the response's stream directly disposes the response correctly, because the client wires stream
// disposal to it, and never materialises the file in memory on this path at all.
//
//
// A SIGNED LINK ALWAYS DOWNLOADS AND NEVER RENDERS INLINE
//
// And the filename in that header is built to the standard rather than interpolated. The name is whatever the
// uploader typed, and a raw quote or a line break in it is header injection; the header builder's own explanation
// covers the defect and why an ASCII-only escape would have been a regression against every Arabic filename in
// this product.

namespace MotsSupplierPortal.Infrastructure.Storage;

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Application.Common;

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
        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _useSsl ? Protocol.HTTPS : Protocol.HTTP,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = ContentDisposition.Attachment(downloadFileName),
            },
        });
        return Task.FromResult(url);
    }
}
