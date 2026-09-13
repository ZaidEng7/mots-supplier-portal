using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record RequestInfoCommand(string ReferenceCode, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes);
