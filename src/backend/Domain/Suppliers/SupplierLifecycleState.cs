// Whether an approved supplier may currently trade.
//
//   None          not approved yet, so the question does not apply
//   Active        may be invited and may bid
//   Suspended     temporarily barred, and can be reinstated
//   Deactivated   permanently out, and cannot be undone

namespace MotsSupplierPortal.Domain.Suppliers;

public enum SupplierLifecycleState
{
    None,
    Active,
    Suspended,
    Deactivated,
}
