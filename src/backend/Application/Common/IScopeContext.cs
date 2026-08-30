namespace MotsSupplierPortal.Application.Common;

/// <summary>
/// The caller's row-scoping context, derived from token claims only - never from client input
/// (STORY-01.8.1). Suppliers see only their own SupplierId; back-office users are scoped to their
/// OrganizationId; ministry is read-only cross-organization; platform admin is global.
/// </summary>
public interface IScopeContext
{
    Guid? UserId { get; }
    Guid? SupplierId { get; }
    Guid? OrganizationId { get; }
    bool IsAuthenticated { get; }

    /// <summary>Task #11: lets a handler check a caller's SPECIFIC permission, not just whether
    /// they are staff (SupplierId is null). GetDocumentDownloadUrlHandler treated "is staff" as
    /// "may download any document" on the strength of a comment claiming the endpoint enforced
    /// document.review - it did not (the endpoint only required authentication). This is the same
    /// "perms" claim PermissionEndpointFilter checks at the endpoint gate, exposed for the cases
    /// where the gate itself cannot express the real rule (here: owner OR reviewer, not just
    /// reviewer) and the check has to live in the handler instead.</summary>
    bool HasPermission(string permission);
}
