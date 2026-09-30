// What the ERP writer throws when writing to the ERP is switched off.
//
// IT IS REFUSED HERE, BEFORE ANY REQUEST IS BUILT, and not only where the push decides whether to run. The owner's
// rule is that the switch gates every write the portal makes to the ERP, and a write to somebody else's system cannot
// be taken back from here: the ERP makes a second supplier for a second request, and removing one is the ERP team's
// work. A caller that forgot to ask would otherwise write anyway. Reads are not refused; the import and the connection
// test run while the switch is off.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpWritesOffException()
    : Exception("Creating suppliers in the ERP is switched off on the ERP connection, so nothing was sent to the ERP. "
        + "Turn it on on the integrations screen, with a default ERP supplier group, to allow it.");
