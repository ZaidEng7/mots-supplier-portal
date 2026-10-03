// The recent activity section of the administrator's dashboard: the last day's count and the latest rows. It is
// hidden from a viewer without audit.read.
//
// Last24Hours and Latest cover the same rows: what people and other systems did, in the last 24 hours and as the
// ten newest. Two kinds of row are left out of both. The session rows SessionAuditActions names arrive with every
// working morning and would make the figure follow office hours rather than the work. And the system's own
// bookkeeping, listed in DashboardAuditActions, records the clock ticking rather than anything somebody did.
//
// SystemLast24Hours is what the system wrote on its own in the same 24 hours, its bookkeeping included: the hourly
// ERP sync opening and closing its run, the push's attempts, the expiry rule and the tender timeline. It is a
// count of its own so that the hourly sync cannot bury a person's change under its rows, and so that a system that
// has stopped writing anything at all can still be seen.
//
// The row is described in DashboardAuditRowDto.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardRecentActivityDto(
    int Last24Hours,
    int SystemLast24Hours,
    IReadOnlyList<DashboardAuditRowDto> Latest);
