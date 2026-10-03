// The administrator's dashboard: six sections, run side by side, each allowed to fail without taking the others
// with it.
//
// The endpoint is gated on admin.users.manage, and each section on the permission below. Three of them need more
// than the endpoint's own:
//
//   systemHealth      admin.users.manage, the endpoint's own permission
//   erp               admin.integrations.manage
//   peopleAndAccess   admin.users.manage
//   security          audit.read
//   recentActivity    audit.read
//   needsAttention    admin.users.manage
//
// A section the viewer may not see is answered hidden and never started, rather than run and its result thrown
// away, because a section that never runs cannot read a row, hold a connection or write a log line about
// something the viewer may not know.
//
// The gates are written out here rather than declared by each section, so the rule about who sees what lives in
// one file and a replacement section cannot widen it.
//
//
// WHO IS ASKING IS READ ONCE, FIRST
//
// The caller's id and every permission their token carries are copied out of the request before anything starts.
// The sections run on other threads in scopes of their own, and are handed that copy rather than the request.
//
// The copy is taken by asking about each permission in the catalogue in turn. A token cannot carry one outside it,
// because a role's permissions are checked against the same catalogue when they are set.
//
//
// EACH SECTION GETS ITS OWN SCOPE, AND SO ITS OWN DATABASE CONTEXT
//
// A database context serves one query at a time. Five sections sharing the request's context would either queue
// behind one another or fail with the framework's second-operation error, so each section is resolved from a
// child scope created for it and disposed when it finishes.
//
// Each one is also started with Task.Run. A section that does synchronous work before its first await, such as
// the job scheduler's monitoring calls, which have no asynchronous form, would otherwise hold up the sections
// started after it, and the parallel start would be parallel only on paper.
//
// That is up to five database contexts open at once for one request, plus the job scheduler's own connections.
// The screen is meant to refresh itself every minute in every open tab, which is why each section is expected to
// keep to a handful of short queries.
//
//
// A SECTION THAT THROWS IS FAILED, AND THE ANSWER STILL ARRIVES
//
// The exception is logged with the section's name and goes no further: the section is answered failed with no
// data and no message, and the other sections are untouched.
//
// Cancellation is the exception to that. When the request itself is cancelled, the caller has gone, and an answer
// made of six failed sections would be a lie told to nobody, so it is allowed to propagate.
//
//
// NEEDS ATTENTION RUNS LAST
//
// It is computed from the other five results, so it starts only when all five have finished, and it is handed all
// five, hidden and failed included. It is resolved from a scope of its own like the others, so a check that needs
// a row of its own can still read one. If it throws, it too is failed on its own.
//
//
// EVERY SECTION CLASS IS NAMED AS A HANDLER, DELIBERATELY
//
// The row-scope guard reads classes whose names contain Handler. Sections read tables the guard watches, the
// supplier documents and the suppliers among them, so a section named anything else would read every supplier's
// rows without the guard ever seeing it. Named as handlers, a section that reads one has to be listed in the
// guard's exemptions with its reason, like every other platform-wide read.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotsSupplierPortal.Application.Admin.Dashboard;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Identity;

public sealed class GetAdminDashboardHandler(
    IScopeContext scope,
    IServiceScopeFactory scopeFactory,
    ILogger<GetAdminDashboardHandler> logger)
    : IGetAdminDashboardHandler
{
    public async Task<AdminDashboardDto> HandleAsync(CancellationToken ct)
    {
        var viewer = new DashboardViewer(
            scope.UserId,
            Permissions.All.Where(scope.HasPermission).ToHashSet(StringComparer.Ordinal));
        var request = new DashboardRequest(viewer, DateTimeOffset.UtcNow);

        var systemHealth = Start<DashboardSystemHealthDto>(
            "systemHealth", viewer.HasPermission(Permissions.AdminUsersManage), request, ct);
        var erp = Start<DashboardErpDto>(
            "erp", viewer.HasPermission(Permissions.AdminIntegrationsManage), request, ct);
        var peopleAndAccess = Start<DashboardPeopleAndAccessDto>(
            "peopleAndAccess", viewer.HasPermission(Permissions.AdminUsersManage), request, ct);
        var security = Start<DashboardSecurityDto>(
            "security", viewer.HasPermission(Permissions.AuditRead), request, ct);
        var recentActivity = Start<DashboardRecentActivityDto>(
            "recentActivity", viewer.HasPermission(Permissions.AuditRead), request, ct);

        await Task.WhenAll(systemHealth, erp, peopleAndAccess, security, recentActivity);

        var others = new DashboardSectionResults(
            await systemHealth, await erp, await peopleAndAccess, await security, await recentActivity);

        var needsAttention = await RunAsync(
            "needsAttention",
            viewer.HasPermission(Permissions.AdminUsersManage),
            services => services.GetRequiredService<INeedsAttentionSectionHandler>().RunAsync(request, others, ct),
            ct);

        return new AdminDashboardDto(
            request.AsOf,
            others.SystemHealth,
            others.Erp,
            others.PeopleAndAccess,
            others.Security,
            others.RecentActivity,
            needsAttention);
    }

    private Task<DashboardSection<TData>> Start<TData>(
        string name, bool visible, DashboardRequest request, CancellationToken ct)
        where TData : class =>
        RunAsync(
            name,
            visible,
            services => services.GetRequiredService<IDashboardSectionHandler<TData>>().RunAsync(request, ct),
            ct);

    private Task<DashboardSection<TData>> RunAsync<TData>(
        string name, bool visible, Func<IServiceProvider, Task<TData>> run, CancellationToken ct)
        where TData : class
    {
        if (!visible)
        {
            return Task.FromResult(DashboardSection<TData>.Hidden());
        }

        return Task.Run(
            async () =>
            {
                try
                {
                    await using var child = scopeFactory.CreateAsyncScope();
                    return DashboardSection<TData>.Ok(await run(child.ServiceProvider));
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(exception, "The admin dashboard's {Section} section failed.", name);
                    return DashboardSection<TData>.Failed();
                }
            },
            ct);
    }
}
