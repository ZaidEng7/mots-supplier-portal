// The object store's address, credentials and bucket.
//
// Every field except the transport flag is required, so binding fails the moment the section is missing or
// incomplete rather than at the first upload.

namespace MotsSupplierPortal.Infrastructure.Storage;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    public required string Endpoint { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public bool UseSsl { get; init; }
    public required string Bucket { get; init; }
}
