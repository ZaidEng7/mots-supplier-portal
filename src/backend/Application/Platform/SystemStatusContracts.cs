namespace MotsSupplierPortal.Application.Platform;

/// <summary>
/// SCR-045: what the global chrome needs to know, for the caller who is asking.
///
/// <para><b>Row-scoped, like everything else.</b> "Is ERP degraded" has a different answer per
/// persona, and the widest one is not the safe default: a supplier learning that some other
/// supplier's award failed to sync has learned that the award exists. The supplier dashboard already
/// makes this distinction (see <c>SupplierDashboardHandler</c>'s own note); this carries the same
/// rule into chrome so the banner is not a second, looser implementation of it.</para>
///
/// <para>The namespace is <c>Platform</c> rather than <c>System</c> deliberately — a namespace of
/// that name shadows the BCL's and breaks every unqualified <c>System.Linq</c> in the assembly.</para>
/// </summary>
/// <param name="ErpDegraded">A sync this caller is entitled to know about has failed.</param>
/// <param name="ErpNotConfigured">No real ERP transport is registered at all — every message is
/// logged and sent nowhere (T-089). Only surfaced to callers who can act on it, because to everyone
/// else it is a deployment fact rather than something about their own work.</param>
public sealed record SystemStatusDto(bool ErpDegraded, bool ErpNotConfigured);

public interface ISystemStatusHandler
{
    Task<SystemStatusDto> HandleAsync(CancellationToken ct);
}
