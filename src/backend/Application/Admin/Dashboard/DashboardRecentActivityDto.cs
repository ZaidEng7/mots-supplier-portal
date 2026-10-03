// The recent activity section of the administrator's dashboard: the last day's count and the latest rows. It is
// hidden from a viewer without audit.read.
//
// Empty in this commit. The section's figures are added by the change that builds the section. The frame around
// it is already in place: the status, the permission that hides it, and the separate scope it runs in, all of
// which are described in AdminDashboardContracts.cs.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardRecentActivityDto;
