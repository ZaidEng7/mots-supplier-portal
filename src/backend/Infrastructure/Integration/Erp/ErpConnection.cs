// Which address and credential the ERP client should actually use, and where they came from.
//
// THE DATABASE WINS THE MOMENT SOMEBODY SAVES AN ADDRESS, and not before. A deployment that has never opened the
// integrations screen keeps running from its own settings, so adding this screen changed nothing for anybody
// already working. The switch is one field - a base URL that is not blank - because a rule spread across several
// fields is one nobody can hold in their head, and the screen has to be able to say which source is in force.
//
// THE SOURCE TRAVELS WITH THE VALUES because the screen shows it. The failure this prevents is somebody editing
// the URL, saving, seeing nothing change, and having no way to discover that an environment variable is still in
// charge.
//
// RESOLVING HAPPENS PER CALL, NOT AT STARTUP. Deciding at boot whether the ERP exists is fine for configuration and
// wrong the moment the values live in a table: an administrator would save an address and the application would
// carry on as though there were none until somebody restarted it.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed record ErpConnection(
    string BaseUrl,
    string ApiKey,
    string ApiSecret,
    bool IsEnabled,
    ErpConnectionSource Source);

public enum ErpConnectionSource
{
    Configuration,
    Database,
}

public interface IErpConnectionProvider
{
    Task<ErpConnection?> CurrentAsync(CancellationToken ct);
}
