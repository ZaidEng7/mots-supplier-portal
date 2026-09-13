using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record UploadDocumentCommand(
    Guid DocumentTypeId,
    Stream Content,
    string OriginalFileName,
    string DeclaredContentType,
    long SizeBytes,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate);
