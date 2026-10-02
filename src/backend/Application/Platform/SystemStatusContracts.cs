// What the banner across the top of every screen needs to know, for the person who is asking.
//
// It is scoped to the caller like everything else, and the widest answer is not the safe default. A
// supplier learning that some other supplier's award failed to sync has learned that the award exists.
// The supplier's own dashboard already draws that line, and this carries the same rule into the banner
// so it is not a second, looser copy of it.
//
// ErpDegraded means a sync this caller is entitled to know about has failed. For the platform
// administrator, who belongs to no organisation, that is any award's in the registry.
//
// ErpNotConfigured means purchase orders are not sent to the ERP in this environment. It is read off the
// transport, and while that is the logging stand-in the finance-side delivery is not built, so an award's
// purchase order is logged and sent nowhere. It used to be read as no connection to the ERP at all, which
// stopped being true when the supplier import and push began talking to the ERP through their own
// connection; it says nothing about those. It is shown only to callers who can act on it, because to
// everybody else it is a fact about the deployment rather than about their own work.
//
// The namespace is Platform rather than System deliberately. A namespace of that name shadows the
// framework's own and breaks every unqualified reference to it in the project.

namespace MotsSupplierPortal.Application.Platform;

public sealed record SystemStatusDto(bool ErpDegraded, bool ErpNotConfigured);

public interface ISystemStatusHandler
{
    Task<SystemStatusDto> HandleAsync(CancellationToken ct);
}
