// What the ERP client throws when there is no connection to use.
//
// IT THROWS RATHER THAN RETURNING AN EMPTY LIST, and that distinction is the reason this type exists. An empty
// list is a truthful answer to "what suppliers does the ERP have" only if the ERP has none, and a preview
// reporting "0 suppliers, nothing to import" when the truth is "nobody configured the credential" reads like
// success. Somebody would forward it.
//
// THE MESSAGE NAMES BOTH PLACES A CONNECTION CAN COME FROM, because after the integrations screen there are two,
// and an administrator who has just saved an address needs to know whether the application disagrees.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpNotConfiguredException()
    : Exception("No ERP connection is configured or enabled: set it on the integrations screen, or supply "
        + "Erp:BaseUrl, Erp:ApiKey and Erp:ApiSecret in the deployment settings.");
