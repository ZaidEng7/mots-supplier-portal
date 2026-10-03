// The security section of the administrator's dashboard: sign-in and session events over 24 hours and 7 days,
// and the latest sensitive changes. It is hidden from a viewer without audit.read.
//
// Events holds one count per event in DashboardAuditActions.SecurityEvents, always all seven and always in that
// order, a zero included. A count that is missing would read as "nothing happened" just as a zero does, and only
// one of them has been counted.
//
// The counts start on the day the sign-in rows began to be stored. Before that the rows were written to memory and
// dropped with the request, so a zero for a week that reaches back past that day is a zero nobody counted.
//
// SensitiveChanges is the ten latest rows whose action is on the allow-list in DashboardAuditActions, newest first,
// each naming who made the change. The row is described in DashboardAuditRowDto.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardSecurityDto(
    IReadOnlyList<DashboardSecurityEventCountDto> Events,
    IReadOnlyList<DashboardAuditRowDto> SensitiveChanges);

public sealed record DashboardSecurityEventCountDto(string Action, int Last24Hours, int Last7Days);
