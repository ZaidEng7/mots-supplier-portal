// What stands in for the ERP client when the integration is switched off.
//
// WHY A STAND-IN AND NOT A MISSING REGISTRATION. The first version of this left the client unregistered when
// Erp:Enabled was false, which is the tidy-looking answer and breaks the API on boot: the preview handler depends
// on the client, the container validates every registration at startup, and a dependency nobody registered is a
// startup failure for every deployment that has not configured an ERP. That is the whole product down over an
// integration that is meant to be optional. The container's own validation caught it, which is the second time
// that check has paid for itself.
//
// IT THROWS RATHER THAN RETURNING AN EMPTY LIST, and that distinction is the only reason this class is worth
// having. An empty list is a truthful answer to "what suppliers does the ERP have" only if the ERP has none - and
// a preview reporting "0 suppliers, nothing to import" when the truth is "nobody configured the credential" is a
// report that reads like success. Somebody would forward it.
//
// THE MESSAGE NAMES THE CONFIGURATION KEY because the person who sees it is an administrator looking at a
// deployment, not a developer reading this file.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using MotsSupplierPortal.Application.Integration;

public sealed class ErpNotConfiguredException()
    : Exception("The ERP integration is switched off: set Erp:Enabled and supply Erp:BaseUrl, Erp:ApiKey and "
        + "Erp:ApiSecret.");

public sealed class DisabledErpSupplierSource : IErpSupplierSource
{
    public Task<IReadOnlyList<ErpSupplier>> ListSuppliersAsync(CancellationToken ct) =>
        throw new ErpNotConfiguredException();
}
