// The security section of the administrator's dashboard: sign-in and session events over 24 hours and 7 days,
// and the latest sensitive changes. It is hidden from a viewer without audit.read.
//
// Events holds one count per event in DashboardAuditActions.SecurityEvents, always all seven and always in that
// order, a zero included. A count that is missing would read as "nothing happened" just as a zero does, and only
// one of them has been counted.
//
//
// A ZERO IS ONLY AS OLD AS COUNTEDSINCE
//
// The counts start on the day the sign-in rows began to be stored. Before that the rows were written to memory and
// dropped with the request, so a zero for a week that reaches back past that day is a zero nobody counted.
//
// CountedSince is that day as the table itself shows it: the time of the oldest stored sign-in attempt, a success
// or any of the four refusals in DashboardAuditActions.SignInAttempts. Once the fix is deployed, every attempt on an
// existing account that gets as far as its password writes one of those rows, so the first one stored is the first
// sign-in after the deploy. It is null when no attempt has been stored at all, and then every figure here is a zero
// nobody counted. A screen says "counted since" with it whenever it falls inside the 7 days, rather than presenting
// a week that is partly empty as a quiet one.
//
// A wrong password typed for an address that has no account writes no row, then or now, so guessing at addresses
// is not in these counts.
//
// A password reset was lost the same way before the same fix, so the one date covers it too. An administrator
// removing a second factor was always stored, and is the one count that may reach back further.
//
//
// SPIKING
//
// Each event says whether it is spiking, by the rule in DashboardSecuritySpike, judged over its 24-hour count and
// its rows from CountedSince up to those 24 hours, so a count that reaches back further than CountedSince does not
// raise the average the rule compares against. Needs attention raises "a security spike" when any event is.
//
//
// THE SENSITIVE CHANGES
//
// SensitiveChanges is the ten latest rows whose action is on the allow-list in DashboardAuditActions, newest first,
// each naming who made the change. The row is described in DashboardAuditRowDto.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardSecurityDto(
    DateTimeOffset? CountedSince,
    IReadOnlyList<DashboardSecurityEventCountDto> Events,
    IReadOnlyList<DashboardAuditRowDto> SensitiveChanges);

public sealed record DashboardSecurityEventCountDto(string Action, int Last24Hours, int Last7Days, bool Spiking);
