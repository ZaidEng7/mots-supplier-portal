namespace MotsSupplierPortal.Application.Admin;

public interface IGetJobsMonitorHandler
{
    JobsMonitorDto Handle();
}

public interface ITriggerRecurringJobHandler
{
    /// <summary>False when the job is not registered - triggering something Hangfire does not have would
    /// report success for nothing happening.</summary>
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
