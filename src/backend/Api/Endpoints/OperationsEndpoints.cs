using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Application.Admin;
using MotsSupplierPortal.Domain.Common;
using MotsSupplierPortal.Domain.Identity;

namespace MotsSupplierPortal.Api.Endpoints;

/// <summary>
/// SCR-721 and SCR-722: the background jobs monitor and the outbox inspector.
///
/// <para>T-083 left both as counters on the admin dashboard - "six jobs registered", "n pending" - which
/// tells an operator that something is wrong and nothing about what. These are the per-row views, and
/// the two actions that exist to take: run a job now, and replay a failed message.</para>
///
/// <para>Same permission as the dashboard they extend (AdminUsersManage, system_admin-only in the
/// catalogue). Not a new permission: this is the same authority looking closer at the same facts.</para>
/// </summary>
public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin");

        group.MapGet("/jobs", (IGetJobsMonitorHandler handler) => Results.Ok(handler.Handle()))
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("GetJobsMonitor");

        group.MapPost("/jobs/{jobId}/trigger", (string jobId, ITriggerRecurringJobHandler handler) =>
            // 404 for a job Hangfire does not hold, rather than a cheerful 202 for nothing happening.
            // An operator pressing Run now on a job a deployment dropped needs to learn that.
            handler.Handle(jobId) ? Results.Accepted() : Results.NotFound())
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("TriggerRecurringJob");

        group.MapGet("/outbox", async (string? status, IGetOutboxMonitorHandler handler, CancellationToken ct) =>
        {
            // Reused rather than reinvented, and the architecture guard is what caught it: the first
            // version of this handler did Enum.TryParse and ignored failure, so ?status=Faild applied no
            // predicate and returned EVERYTHING - an operator looking for stuck messages would read a
            // full list as "nothing is filtered" or, worse, glance at it and conclude nothing failed. A
            // filter whose unrecognised value widens the result is not a filter. 422, same as every
            // other list in this API, and TryParseEnumCsv brings §6.2's multi-value OR with it.
            if (!FilterValues.TryParseEnumCsv<OutboxSyncStatus>(status, out _, out var invalid))
            {
                return FilterValues.InvalidFilterValue("status", invalid!);
            }

            return Results.Ok(await handler.HandleAsync(status, ct));
        })
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("GetOutboxMonitor");

        // SCR-723. The retry action is NOT here: POST /awards/{code}/retry-erp-sync already exists behind
        // integration.retry and enforces §6.1's "only a Failed sync retries". The screen calls that one.
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

        // SCR-726. A READ, with no write beside it, and the screen explains why: every value here is
        // deployment configuration, and putting a password floor or an MFA requirement behind an admin
        // click would move a security decision from a reviewed deployment to a runtime action - the first
        // thing an attacker holding an admin session would reach for.
        group.MapGet("/security", async (IGetSecurityPostureHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .RequirePermission(Permissions.AdminUsersManage)
        .WithName("GetSecurityPosture");

        group.MapPost("/outbox/{id:guid}/replay", async (Guid id, IReplayOutboxMessageHandler handler, CancellationToken ct) =>
            // One 404 for two cases - no such message, and a message that is not Failed - and that is
            // deliberate rather than lazy: both mean "there is nothing here to replay", and splitting
            // them would invite a caller to branch on a distinction that does not change what to do.
            await handler.HandleAsync(id, ct) ? Results.Accepted() : Results.NotFound())
            .RequirePermission(Permissions.AdminUsersManage)
            .WithName("ReplayOutboxMessage");
    }
}
