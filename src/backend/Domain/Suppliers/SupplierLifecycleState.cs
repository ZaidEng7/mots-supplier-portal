namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>Post-approval lifecycle (docs/architecture/00-foundational-decisions.md §5).</summary>
public enum SupplierLifecycleState
{
    None,
    Active,
    Suspended,
    Deactivated,
}
