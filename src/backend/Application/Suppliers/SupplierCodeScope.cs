// The check that a supplier code in a path belongs to the caller.
//
//
// WHY THIS TYPE EXISTS AT ALL
//
// The old path shape had no slot for another supplier's identifier, so the attack could not be expressed and
// no check was needed.
//
// The current shape hands every caller a way to name a supplier that is not theirs. Cross-tenant leakage is
// the only critical entry on the risk register, so the replacement for a structural guarantee is one
// auditable check in one place, rather than a condition repeated at six call sites.
//
// Both methods answer the same way for a code that does not exist and a code belonging to somebody else. An
// out-of-scope read of something real must be indistinguishable from a read of something absent, or the
// refusal itself becomes the disclosure.

namespace MotsSupplierPortal.Application.Suppliers;

public interface ISupplierCodeScope
{
    Task<Guid?> ResolveOwnAsync(string supplierCode, CancellationToken ct);

    Task<bool> DocumentBelongsToSupplierAsync(string supplierCode, string documentCode, CancellationToken ct);
}
