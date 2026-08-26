using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Audit;

public sealed class GetAuditLogHandler(AppDbContext db) : IGetAuditLogHandler
{
    public async Task<IReadOnlyList<AuditLogEntryDto>> HandleAsync(Guid aggregateId, CancellationToken ct)
    {
        return await db.AuditLogs
            .Where(a => a.AggregateId == aggregateId)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new AuditLogEntryDto(a.OccurredAt, a.AggregateType, a.AggregateId, a.Action, a.FromState, a.ToState, a.ActorLabel))
            .ToListAsync(ct);
    }
}
