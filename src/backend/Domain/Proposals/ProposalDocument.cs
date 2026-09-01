namespace MotsSupplierPortal.Domain.Proposals;

/// <summary>FEAT-09.3/FR-PRP-004: compliance/technical supporting files - part of the two-envelope
/// TECHNICAL content (never pricing). Stored via the existing IFileStorage, same pattern as
/// RfqAttachment/SupplierDocument - no new storage mechanism invented.</summary>
public sealed class ProposalDocument
{
    public Guid Id { get; init; }
    public Guid ProposalId { get; init; }
    public string StorageKey { get; init; } = null!;
    public string OriginalFileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public string? Caption { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
}
