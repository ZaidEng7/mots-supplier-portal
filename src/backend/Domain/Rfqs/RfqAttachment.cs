namespace MotsSupplierPortal.Domain.Rfqs;

/// <summary>An RFQ-level specification/document (DOMAIN-MODEL.md §5.4), linked to the shared
/// Document abstraction via IFileStorage - same pattern as SupplierDocument/ProposalDocument.</summary>
public sealed class RfqAttachment
{
    public Guid Id { get; init; }
    public Guid RfqId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; set; }
    public DateTimeOffset UploadedAt { get; init; }
}
