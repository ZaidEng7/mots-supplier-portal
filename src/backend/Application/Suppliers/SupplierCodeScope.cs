namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>
/// §12-A/C3: the row-scope check that the <c>me</c> path shape used to make unnecessary.
///
/// <para><b>Why this type exists at all.</b> <c>/suppliers/me/documents</c> has no slot for another
/// supplier's identifier - the attack could not be expressed, so no check was needed.
/// <c>/suppliers/{supplierCode}/documents</c> (§12.3) hands every caller a way to name a supplier
/// that is not theirs. RISK-004 (cross-tenant leakage) is the risk register's only Critical entry,
/// so the replacement for a structural guarantee is one auditable check, in one place, rather than
/// a condition repeated at six call sites.</para>
///
/// <para><b>Both methods answer "no" the same way for unknown and out-of-scope.</b> §9.2:
/// *"Out-of-scope access to an existing resource returns 404 (not 403) to avoid leaking
/// existence."* A caller must not be able to tell a supplier code that does not exist from one that
/// exists and is someone else's, so neither method distinguishes them and the endpoints map both to
/// 404.</para>
/// </summary>
public interface ISupplierCodeScope
{
    /// <summary>
    /// The supplier id behind <paramref name="supplierCode"/>, but only when it is the CALLER'S OWN
    /// supplier. Null when the code is unknown, when it belongs to someone else, or when the caller
    /// has no supplier scope at all (staff).
    /// </summary>
    Task<Guid?> ResolveOwnAsync(string supplierCode, CancellationToken ct);

    /// <summary>
    /// True only when <paramref name="documentId"/> exists AND belongs to the supplier named by
    /// <paramref name="supplierCode"/>. Used by the reviewer-facing document transitions, where the
    /// caller is staff acting on any supplier - so the check is not "is this mine" but "does the
    /// path name the document's real owner". Without it, a reviewer could approve supplier B's
    /// document through supplier A's URL and the audit trail would record the wrong supplier.
    /// </summary>
    Task<bool> DocumentBelongsToSupplierAsync(string supplierCode, Guid documentId, CancellationToken ct);
}
