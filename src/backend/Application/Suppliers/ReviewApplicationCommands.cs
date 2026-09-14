// What a reviewer asks for when they need more information: the reason, and which profile fields and document
// types they are flagging.
//
// The flagged lists are what decide which fields the supplier may then edit, so they are part of the request
// rather than prose inside the reason.

namespace MotsSupplierPortal.Application.Suppliers;

using MotsSupplierPortal.Application.Common;

public sealed record RequestInfoCommand(string ReferenceCode, string Reason, IReadOnlyList<string> FlaggedProfileFields, IReadOnlyList<string> FlaggedDocumentTypeCodes);
