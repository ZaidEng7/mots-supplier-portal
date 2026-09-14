// What is needed to upload one compliance document: the file, its type, and the dates on it.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Suppliers;

public sealed record UploadDocumentCommand(
    Guid DocumentTypeId,
    Stream Content,
    string OriginalFileName,
    string DeclaredContentType,
    long SizeBytes,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate);
