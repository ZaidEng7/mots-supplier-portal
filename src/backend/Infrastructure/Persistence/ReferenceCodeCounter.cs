// The allocation state behind the public reference codes. One row per prefix, holding the highest value issued.
//
// A persistence mechanism rather than a domain concept, which is why it lives here rather than in the domain:
// nothing in the business model asks how a code is allocated, only that codes are unique.
//
// Keyed by the full prefix including its year, so the year rollover is an ordinary new row with no special
// handling, and a second code series can share the mechanism without a schema change.
//
// The value is monotonic: only ever incremented, never recomputed from the rows that currently exist, which is
// precisely the defect this table replaces.

namespace MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ReferenceCodeCounter
{
    public string Prefix { get; set; } = string.Empty;

    public long LastValue { get; set; }
}
