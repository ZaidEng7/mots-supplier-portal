// The vocabulary for suspending, reinstating and deactivating an approved supplier.
//
// A reason is mandatory on every one of them, including reinstating. The audit trail should record why
// participation was restored and not only why it was removed.
//
// Out of scope and absent are one outcome. Telling an unauthorised caller that a reference code exists is
// itself a disclosure.
//
// An illegal change is a typed refusal carrying the reason rather than a hidden button. The interface may also
// hide the action, but hiding is a convenience and the rule is enforced here.

namespace MotsSupplierPortal.Application.Suppliers;

public sealed record SupplierLifecycleCommand(string ReferenceCode, string Reason);

public abstract record SupplierLifecycleResult
{
    public sealed record Success(string LifecycleState) : SupplierLifecycleResult;

    public sealed record NotFound : SupplierLifecycleResult;

    public sealed record Invalid(string Message) : SupplierLifecycleResult;
}

public interface ISupplierLifecycleHandler
{
    Task<SupplierLifecycleResult> SuspendAsync(SupplierLifecycleCommand command, CancellationToken ct);

    Task<SupplierLifecycleResult> ReactivateAsync(SupplierLifecycleCommand command, CancellationToken ct);

    Task<SupplierLifecycleResult> DeactivateAsync(SupplierLifecycleCommand command, CancellationToken ct);
}
