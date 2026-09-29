// Bringing the ERP's suppliers into the portal every hour.
//
// IT IS THE SAME IMPORT AS THE BUTTON, NOT A SECOND ONE. A scheduled copy of the logic would drift from the one people
// test by hand, and the day it drifted would be a run nobody was watching. So this calls the import handler and
// adds only what running unattended needs: deciding whether to run at all, and saying nothing alarming when the
// answer is no.
//
// NO CONNECTION MEANS NO RUN, QUIETLY. A deployment that has not configured the ERP is not failing every hour - it
// is simply not integrated yet - and a job that recorded a failure every hour would train people to ignore the
// failure that eventually matters.
//
// A CONNECTION THAT IS CONFIGURED BUT BROKEN IS A FAILURE, LOUDLY. The import records it on the connection, where the
// integrations screen shows it, and the exception reaches the scheduler so the run is marked failed there too.
//
// ANOTHER IMPORT ALREADY RUNNING IS NOT A FAILURE. Somebody pressed the button as the hour turned; their run does
// this hour's work, so this one steps aside.
//
// RETRIES ARE LIMITED TO TWO. The import is safe to repeat, so a retry costs nothing but a read of the ERP - but ten,
// the scheduler's default, would turn one misconfigured password into ten identical failures spread over hours.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using Hangfire;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Integration;

public sealed class ErpSupplierSyncJob(
    IErpConnectionProvider connections,
    IRunErpImportHandler import,
    ILogger<ErpSupplierSyncJob> logger)
{
    public const string JobId = "erp-supplier-sync";

    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is null || !connection.IsEnabled)
        {
            logger.LogInformation("Hourly ERP supplier sync skipped: no ERP connection is configured and enabled.");
            return;
        }

        try
        {
            var report = await import.HandleAsync(ErpImportTrigger.Scheduled, ct);

            logger.LogInformation(
                "Hourly ERP supplier sync finished: {Created} created, {Updated} updated, {Suspended} suspended, "
                + "{Failed} failed.",
                report.Created, report.Updated, report.Suspended, report.Failed);
        }
        catch (ErpImportBusyException)
        {
            logger.LogInformation("Hourly ERP supplier sync skipped: another import was already running.");
        }
    }
}
