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
}
