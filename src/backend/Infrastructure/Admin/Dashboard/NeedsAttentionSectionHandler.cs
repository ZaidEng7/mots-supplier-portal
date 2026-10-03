// The needs attention section of the administrator's dashboard, computed from the other five sections' results.
//
// A placeholder in this commit: it answers ok with an empty record so the frame around it can be built and tested
// first. The change that builds this section replaces the body, its data record and this paragraph.
//
// It runs after the other five have finished and is handed all of their results, hidden and failed included,
// as GetAdminDashboardHandler describes. Which links a viewer may follow comes from the viewer's permissions in
// the request, which were read once before any section started.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using MotsSupplierPortal.Application.Admin.Dashboard;

public sealed class NeedsAttentionSectionHandler : INeedsAttentionSectionHandler
{
    public Task<DashboardNeedsAttentionDto> RunAsync(
        DashboardRequest request, DashboardSectionResults others, CancellationToken ct) =>
        Task.FromResult(new DashboardNeedsAttentionDto());
}
