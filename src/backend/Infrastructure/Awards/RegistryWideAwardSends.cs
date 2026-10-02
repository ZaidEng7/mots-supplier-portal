// Who is served the sends of every award in the registry, rather than only their own organisation's: the platform
// administrator. The award's retry and the status banner both ask it, so it is written once and they give the same
// answer.
//
// HAVING NO ORGANISATION IS NOT BEING THE PLATFORM ADMINISTRATOR. A reviewer, an evaluator or the ministry's viewer has
// none either, and so does an account invited without one and later given another role. If such an account's role is
// granted integration.retry, which a deployment may grant an organisation's role so it can retry its own awards, it
// would otherwise retry any organisation's failed award and read that award back in full, probe every other award's
// send state tender code by tender code, since a refusal names the state where a tender with no award is not found, and
// see the whole registry's failures on the banner.
//
// SO THE WIDE PATH ALSO NEEDS admin.integrations.manage, the permission the supplier push's retry is gated by. It is
// the system administrator's alone by default, and it is the one that covers what this product's connection to the
// ERP does across the whole registry rather than within one organisation's rows. A caller with no supplier and no
// organisation who holds both is served across the registry. Anybody with an organisation keeps their own
// organisation's scope whatever they are granted, and a supplier's account never takes this path.

namespace MotsSupplierPortal.Infrastructure.Awards;

using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

internal static class RegistryWideAwardSends
{
    public static bool AreServedTo(IScopeContext scope) =>
        scope.SupplierId is null
        && scope.OrganizationId is null
        && scope.HasPermission(Permissions.IntegrationRetry)
        && scope.HasPermission(Permissions.AdminIntegrationsManage);
}
