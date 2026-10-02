// The ERP section of the administrator's dashboard, hidden from a viewer without admin.integrations.manage.
//
// A placeholder in this commit: it answers ok with an empty record so the frame around it can be built and tested
// first. The change that builds this section replaces the body, its data record and this paragraph. Until then it
// reads nothing, so it needs no exemption in the row-scope guard.
//
// Whatever it becomes, it reads database rows and configuration only. The dashboard never contacts the ERP.
//
// It is resolved from a scope of its own and runs beside the other sections, as GetAdminDashboardHandler
// describes, so it may take a database context in its constructor and use it freely.

namespace MotsSupplierPortal.Infrastructure.Admin.Dashboard;

using MotsSupplierPortal.Application.Admin.Dashboard;

public sealed class ErpSectionHandler : IDashboardSectionHandler<DashboardErpDto>
{
    public Task<DashboardErpDto> RunAsync(DashboardRequest request, CancellationToken ct) =>
        Task.FromResult(new DashboardErpDto());
}
