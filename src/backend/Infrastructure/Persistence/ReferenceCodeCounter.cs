namespace MotsSupplierPortal.Infrastructure.Persistence;

/// <summary>
/// Allocation state for human-facing reference codes (MSP-81). One row per code prefix, e.g.
/// "SUP-2026-", holding the highest value issued so far.
///
/// This is a persistence mechanism, not a domain concept, which is why it lives here rather than in
/// Domain: nothing in the business model asks how a code is allocated, only that codes are unique.
///
/// Keyed by the full prefix rather than by year alone so the year rollover is an ordinary new row
/// with no special handling, and so a second code series (a different entity type, a different
/// format) can share the mechanism without a schema change.
/// </summary>
public sealed class ReferenceCodeCounter
{
    /// <summary>The code prefix including its trailing separator, e.g. "SUP-2026-".</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Highest sequence value issued for this prefix. Monotonic: it is only ever
    /// incremented, never recomputed from the rows that currently exist, which is precisely the
    /// defect this table replaces.</summary>
    public long LastValue { get; set; }
}
