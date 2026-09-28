// Reading the ERP's address and credential from the database, falling back to configuration.
//
// The rule and the reasons are in ErpConnection. This is the part that does it.
//
// A ROW WITH NO SECRET FALLS BACK FOR THE SECRET ALONE, and that is the one place the all-or-nothing rule bends -
// deliberately. The screen cannot show a stored secret, so an administrator moving an address to a new server
// saves the URL first and the credential second, and between those two saves the connection would otherwise have
// no credential at all. Reaching for the configured one keeps it working across that gap, and the screen says a
// secret has not been set here.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Infrastructure.Persistence;

public sealed class ErpConnectionProvider(
    AppDbContext db,
    SecretCipher cipher,
    IOptions<ErpOptions> options) : IErpConnectionProvider
{
    public async Task<ErpConnection?> CurrentAsync(CancellationToken ct)
    {
        var settings = options.Value;

        var row = await db.IntegrationConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == IntegrationConnection.ErpKey, ct);

        if (row is null || !row.IsConfiguredHere)
        {
            return string.IsNullOrWhiteSpace(settings.BaseUrl)
                ? null
                : new ErpConnection(
                    settings.BaseUrl, settings.ApiKey, settings.ApiSecret, settings.Enabled,
                    ErpConnectionSource.Configuration);
        }

        var secret = row.SecretCipher is null ? settings.ApiSecret : cipher.Unprotect(row.SecretCipher);

        return new ErpConnection(row.BaseUrl, row.ApiKey, secret ?? string.Empty, row.IsEnabled,
            ErpConnectionSource.Database);
    }
}
