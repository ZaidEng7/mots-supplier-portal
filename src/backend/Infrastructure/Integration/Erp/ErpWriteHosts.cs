// Whether an ERP address names a server listed in Erp:WriteHosts, the deployment's list of the ERP servers the portal
// may write to. ErpOptions says why the list exists and why it is configuration rather than a field on a screen.
//
// AN ENTRY MATCHES THE SERVER'S HOST, OR ITS HOST AND PORT, in any letter case and with any spaces around it trimmed.
// "9.160.105.219" allows that server on any port, and "9.160.105.219:8001" allows it on that port only. The port is
// compared as the address's authority writes it, so a default port is left out on both sides: an entry
// "erp.example:80" does not match "http://erp.example", whose authority is "erp.example". An address that does not
// parse as an absolute URL names no server, and so is never listed.
//
// TWO PLACES ASK, AND THEY MUST GET THE SAME ANSWER. ErpSupplierRegistrar asks before every write, and refuses one to a
// server that is not listed. The administrator's dashboard asks too, to show a yes or no beside the ERP's host, so that
// somebody can see at a glance whether the portal could write to the server it is pointed at. A dashboard with a
// matching rule of its own could say yes while the writer refuses, or no while the writer writes, which is why the rule
// lives here once.
//
// It answers yes or no, and never the list. The list is the deployment's configuration and stays there; the dashboard
// shows only whether the one server in use is on it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public static class ErpWriteHosts
{
    public static bool Lists(IEnumerable<string> writeHosts, string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var server)
        && writeHosts.Any(host =>
            string.Equals(host.Trim(), server.Authority, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host.Trim(), server.Host, StringComparison.OrdinalIgnoreCase));
}
