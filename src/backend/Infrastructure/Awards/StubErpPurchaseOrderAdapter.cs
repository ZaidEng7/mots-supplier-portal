using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Infrastructure.Awards;

/// <summary>Dev-only stand-in for the real EPIC-23 ERPNext Purchase Order integration - same shape
/// as LoggingOutboxTransport/LoggingEmailSender for their own not-yet-built providers. Always
/// succeeds, returning a fake, obviously-synthetic PO reference. Never logs the RFQ reference code
/// or any award content (MSP-61/BRULE-091) - only the award id, which is meaningless outside this
/// system.</summary>
public sealed class StubErpPurchaseOrderAdapter(ILogger<StubErpPurchaseOrderAdapter> logger) : IErpPurchaseOrderAdapter
{
    public Task<string> CreatePurchaseOrderAsync(Guid awardId, string rfqReferenceCode, CancellationToken ct = default)
    {
        var externalRef = $"PO-STUB-{awardId:N}"[..16];
        logger.LogInformation("Award {AwardId} Purchase Order stub-created as {ExternalPurchaseOrderRef}", awardId, externalRef);
        return Task.FromResult(externalRef);
    }
}
