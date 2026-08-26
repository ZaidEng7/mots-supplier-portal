namespace MotsSupplierPortal.Domain.Suppliers;

/// <summary>
/// Raised when a caller attempts an illegal state transition or violates an aggregate invariant.
/// The domain is the last line of defense — it rejects illegal transitions independent of the UI
/// (docs/backlog/ROADMAP.md §6.2).
/// </summary>
public sealed class DomainException(string message) : Exception(message);
