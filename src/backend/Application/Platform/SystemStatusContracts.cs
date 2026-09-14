// What the banner across the top of every screen needs to know, for the person who is asking.
//
// It is scoped to the caller like everything else, and the widest answer is not the safe default. A
// supplier learning that some other supplier's award failed to sync has learned that the award exists.
// The supplier's own dashboard already draws that line, and this carries the same rule into the banner
// so it is not a second, looser copy of it.
//
// ErpDegraded means a sync this caller is entitled to know about has failed.
//
// ErpNotConfigured means no real connection to the finance system is registered at all, so every message
// is logged and sent nowhere. It is shown only to callers who can act on it, because to everybody else
// it is a fact about the deployment rather than about their own work.
//
// The namespace is Platform rather than System deliberately. A namespace of that name shadows the
// framework's own and breaks every unqualified reference to it in the project.

namespace MotsSupplierPortal.Application.Platform;

public sealed record SystemStatusDto(bool ErpDegraded, bool ErpNotConfigured);

public interface ISystemStatusHandler
{
    Task<SystemStatusDto> HandleAsync(CancellationToken ct);
}
