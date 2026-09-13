// The version the caller believes it is editing, so a write can be refused when somebody else has changed
// the record in between.
//
// It travels as the standard HTTP precondition header rather than as a field on every request body. That
// way the twenty-five writes do not each need a new field, and the meaning is the one the protocol already
// defines for exactly this.
//
// Absent means the caller supplied no version. What a handler does about that is the handler's policy.

namespace MotsSupplierPortal.Application.Common;

public interface IConcurrencyContext
{
    uint? ExpectedRowVersion { get; }
}
