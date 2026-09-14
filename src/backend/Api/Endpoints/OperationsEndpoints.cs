// The operator's screens: the background-jobs monitor, the outbox inspector, and two read-only views of the
// security and file-upload settings.
//
// The admin dashboard already carried counters, six jobs registered and so many messages pending, which tells
// an operator that something is wrong and nothing about what. These are the per-row views, plus the two
// actions worth taking: run a job now, and replay a failed message.
//
// They share the dashboard's permission rather than introducing one. This is the same authority looking
// closer at the same facts.
//
// Running a job that the scheduler does not hold answers not-found rather than a cheerful accepted for
// nothing happening. An operator pressing run on a job that a deployment dropped needs to learn that.
//
// The outbox status filter is parsed through the shared helper rather than reinvented, and the architecture
// guard is what caught the first version: it parsed the value and ignored failure, so a misspelt status
// applied no filter and returned everything. An operator looking for stuck messages would read a full list as
// nothing being filtered or, worse, glance at it and conclude nothing had failed. A filter whose unrecognised
// value widens the result is not a filter.
//
// Replaying a message that does not exist and replaying one that has not failed give the same not-found
// answer. That is deliberate rather than lazy: both mean there is nothing here to replay, and splitting them
// would invite a caller to branch on a distinction that does not change what to do.
//
// The finance-sync retry action is deliberately not here. A route for it already exists on the award, behind
// its own permission, and it enforces that only a failed sync may be retried. The screen calls that one.
//
// The security-settings and file-upload views are reads with no write beside them, and the reason is the same
// for both. Every value on them is deployment configuration, and putting a password floor, a second-factor
// requirement, an upload cap or an allowed-file-type list behind an administrator's click would move a
// security decision from a reviewed deployment into a runtime action, which is the first thing somebody
// holding a stolen administrator session would reach for. The document rules administrators genuinely do own
// live on their own screen.

namespace MotsSupplierPortal.Api.Endpoints;

using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;

public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin");

        group.MapGet("/jobs", (IGetJobsMonitorHandler handler) => Results.Ok(handler.Handle()))
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("GetJobsMonitor");

        group.MapPost("/jobs/{jobId}/trigger", (string jobId, ITriggerRecurringJobHandler handler) =>
            handler.Handle(jobId) ? Results.Accepted() : Results.NotFound())
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("TriggerRecurringJob");

        group.MapGet("/outbox", async (string? status, IGetOutboxMonitorHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseEnumCsv<OutboxSyncStatus>(status, out _, out var invalid))
            {
                return FilterValues.InvalidFilterValue("status", invalid!);
            }

            return Results.Ok(await handler.HandleAsync(status, ct));
        })
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("GetOutboxMonitor");

        group.MapGet("/erp-sync", async (string? status, IGetErpSyncMonitorHandler handler, CancellationToken ct) =>
        {
            if (!FilterValues.TryParseEnumCsv<Domain.Awards.ErpSyncStatus>(status, out _, out var invalid))
            {
                return FilterValues.InvalidFilterValue("status", invalid!);
            }

            return Results.Ok(await handler.HandleAsync(status, ct));
        })
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetErpSyncMonitor");

        group.MapGet("/security", async (IGetSecurityPostureHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetSecurityPosture");

        group.MapGet("/storage", async (IGetStorageSettingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetStorageSettings");

        group.MapPost("/outbox/{id:guid}/replay", async (Guid id, IReplayOutboxMessageHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(id, ct) ? Results.Accepted() : Results.NotFound())
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("ReplayOutboxMessage");
    }
}
