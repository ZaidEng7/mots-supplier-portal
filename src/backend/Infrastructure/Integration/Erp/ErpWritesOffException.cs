// What the ERP writer throws when writing to the ERP is not allowed: the connection's switch is off, or the server it
// points at is not one this deployment may write to.
//
// IT IS REFUSED HERE, BEFORE ANY REQUEST IS BUILT, and not only where the push decides whether to run. The owner's
// rule is that the switch gates every write the portal makes to the ERP, and a write to somebody else's system cannot
// be taken back from here: the ERP makes a second supplier for a second request, and removing one is the ERP team's
// work. A caller that forgot to ask would otherwise write anyway. Reads are not refused; the import and the connection
// test run while the switch is off.
//
// THE SERVER MUST ALSO BE LISTED, in Erp:WriteHosts (ErpOptions explains why). The two refusals share this type because
// they mean the same thing to the push: stop the run and change no supplier, since nothing reached the ERP and the next
// run can go ahead once the setting is right.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpWritesOffException(string message) : Exception(message)
{
    public ErpWritesOffException()
        : this("Creating suppliers in the ERP is switched off on the ERP connection, so nothing was sent to the ERP. "
            + "Turn it on on the integrations screen, with a default ERP supplier group, to allow it.")
    {
    }

    public static ErpWritesOffException ServerNotAllowed(string server) =>
        new($"This deployment may not write to the ERP at {server}, so nothing was sent to the ERP. "
            + "Add the server to Erp:WriteHosts in the deployment's configuration to allow it.");
}
