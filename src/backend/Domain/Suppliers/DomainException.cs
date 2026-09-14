// Raised when a caller attempts an illegal state change or breaks one of a record's own rules.
//
// The domain is the last line of defence. It refuses an illegal move regardless of what the interface
// allowed.

namespace MotsSupplierPortal.Domain.Suppliers;

public sealed class DomainException(string message) : Exception(message);
