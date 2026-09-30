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
// THE LOCK TAKEN IS NOT A FAILURE, AND NOT A LOST HOUR EITHER. ErpImportLock is held by an import somebody started by
// hand as the hour turned, or by a supplier push to the ERP, which runs straight after an approval and every five
// minutes. A push does none of the import's work, so stepping aside until the next hour would hold back that hour's
// releases, suspensions and updates for nothing. The job tries once more RetryAfterBusy later, which is long enough
// for a push of a few suppliers to finish. That second try steps aside if the lock is still taken, and schedules
// nothing more, so a lock held for a long time never builds a queue of imports behind it; the next hour runs as usual.
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
    IBackgroundJobClient backgroundJobs,
    ILogger<ErpSupplierSyncJob> logger)
{
    public const string JobId = "erp-supplier-sync";

    public static readonly TimeSpan RetryAfterBusy = TimeSpan.FromMinutes(2);

    [AutomaticRetry(Attempts = 2)]
    public Task RunAsync(CancellationToken ct = default) => ImportAsync(tryAgainWhenBusy: true, ct);

    [AutomaticRetry(Attempts = 2)]
    public Task RunAgainAsync(CancellationToken ct = default) => ImportAsync(tryAgainWhenBusy: false, ct);

    private async Task ImportAsync(bool tryAgainWhenBusy, CancellationToken ct)
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
            if (!tryAgainWhenBusy)
            {
                logger.LogInformation(
                    "Hourly ERP supplier sync skipped: an import or a supplier push was still running on the second "
                    + "try; the next hour's run carries on.");
                return;
            }

            backgroundJobs.Schedule<ErpSupplierSyncJob>(
                job => job.RunAgainAsync(CancellationToken.None), RetryAfterBusy);

            logger.LogInformation(
                "Hourly ERP supplier sync waits: an import or a supplier push is running, so it tries once more in "
                + "{Minutes} minutes.",
                RetryAfterBusy.TotalMinutes);
        }
    }
}
