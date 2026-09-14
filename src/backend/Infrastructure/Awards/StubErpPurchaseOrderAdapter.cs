// The stand-in for the real purchase-order integration, for development.
//
// The same shape the logging transport and the logging email sender have for their own not-yet-built providers.
// It always succeeds and returns an obviously synthetic reference.
//
// It never logs the tender's reference code or any award content. Only the award's internal identifier, which is
// meaningless outside this system.

namespace MotsSupplierPortal.Infrastructure.Awards;

using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

public sealed class StubErpPurchaseOrderAdapter(ILogger<StubErpPurchaseOrderAdapter> logger) : IErpPurchaseOrderAdapter
{
    public Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default)
    {
        var externalRef = $"PO-STUB-{awardId:N}"[..16];
        logger.LogInformation("Award {AwardId} Purchase Order stub-created as {ExternalPurchaseOrderRef}", awardId, externalRef);
        return Task.FromResult(externalRef);
    }
}
