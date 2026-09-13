// What the operator's reads and the two operator actions are called.
//
// Triggering a job answers false when the job is not registered. Reporting success for nothing happening
// would leave an operator believing they had acted.

namespace MotsSupplierPortal.Application.Admin;

public interface IGetJobsMonitorHandler
{
    JobsMonitorDto Handle();
}

public interface ITriggerRecurringJobHandler
{
    bool Handle(string jobId);
}

public interface IGetOutboxMonitorHandler
{
    Task<OutboxMonitorDto> HandleAsync(string? status, CancellationToken ct);
}

public interface IReplayOutboxMessageHandler
{
    Task<bool> HandleAsync(Guid id, CancellationToken ct);
}

public interface IGetErpSyncMonitorHandler
{
    Task<ErpSyncMonitorDto> HandleAsync(string? status, CancellationToken ct);
}

public interface IGetSecurityPostureHandler
{
    Task<SecurityPostureDto> HandleAsync(CancellationToken ct);
}

public interface IGetStorageSettingsHandler
{
    Task<StorageSettingsDto> HandleAsync(CancellationToken ct);
}
