namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>FR-ONB-009 / BRULE-096: reason is mandatory on every lifecycle transition, including
/// reactivation - the audit trail should record why participation was restored, not only why it
/// was removed.</summary>
public sealed record SupplierLifecycleCommand(string ReferenceCode, string Reason);

public abstract record SupplierLifecycleResult
{
    public sealed record Success(string LifecycleState) : SupplierLifecycleResult;

    /// <summary>Out of scope or absent. Deliberately one result rather than two: telling an
    /// unauthorised caller that a reference code exists is itself a disclosure.</summary>
    public sealed record NotFound : SupplierLifecycleResult;

    /// <summary>NFR-CMP-003 / BRULE-097: an illegal transition is a typed domain error carrying the
    /// reason, not a hidden button. The UI may also hide the action, but hiding is a convenience -
    /// the rule is enforced here.</summary>
    public sealed record Invalid(string Message) : SupplierLifecycleResult;
}

public interface ISupplierLifecycleHandler
{
    /// <summary>Active -> Suspended (FR-ONB-009). Reversible.</summary>
    Task<SupplierLifecycleResult> SuspendAsync(SupplierLifecycleCommand command, CancellationToken ct);

    /// <summary>Suspended -> Active (FR-ONB-009).</summary>
    Task<SupplierLifecycleResult> ReactivateAsync(SupplierLifecycleCommand command, CancellationToken ct);

    /// <summary>Suspended -> Deactivated (FR-ONB-009). Terminal, and revokes the supplier's users'
    /// access per BRULE-008.</summary>
    Task<SupplierLifecycleResult> DeactivateAsync(SupplierLifecycleCommand command, CancellationToken ct);
}
