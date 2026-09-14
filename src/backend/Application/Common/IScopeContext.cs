// Which rows the caller may see, taken from their token's claims and never from anything a client sent.
//
// A supplier sees only their own company. Buying staff are scoped to their organization. The ministry reads
// across organizations and writes nothing. A platform administrator is unscoped.
//
// HasPermission lets a handler check one specific permission rather than merely asking whether the caller
// is staff. It reads the same claim the route filter checks, exposed for the cases where the route cannot
// express the real rule.
//
// It exists because of a real defect. A document download treated "is staff" as "may download any
// document", on the strength of a comment claiming the route enforced a review permission. It did not; the
// route only required being signed in. The real rule there is the document's owner or a reviewer, which a
// route-level gate cannot say, so the check has to live in the handler.

namespace MotsSupplierPortal.Application.Common;

public interface IScopeContext
{
    Guid? UserId { get; }
    Guid? SupplierId { get; }
    Guid? OrganizationId { get; }
    bool IsAuthenticated { get; }

    bool HasPermission(string permission);
}
