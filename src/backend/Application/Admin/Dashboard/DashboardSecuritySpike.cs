// When one security event counts as a spike on the administrator's dashboard: what needs attention reads to raise
// "a security spike", with a link to the audit search for a viewer who holds audit.read.
//
// The specification names the item and gives no rule for it, so the rule is chosen here, kept simple, and
// written down so it can be argued with. An event is spiking when both of these hold:
//
//   at least MinimumLast24Hours of it in the last 24 hours, so that two wrong passwords on a quiet day are not an
//   alarm however quiet the week before was; and
//
//   at least SpikeFactor times its daily average over the six days before those 24 hours, so that a portal where
//   forty wrong passwords a day is normal does not raise the item every day.
//
// The average is taken only over the part of those six days in which the rows were being stored. The sign-in rows
// began to be stored on the day the fix that saves them was deployed; before that they were written to memory and
// dropped. Six empty days that nobody counted would make any figure look like a spike, so the average divides by
// the days actually counted, and with less than one whole day of history before the last 24 hours nothing is
// judged a spike at all. The first day after the deploy cannot raise the item.
//
// Each event is judged on its own. A wave of wrong passwords and a wave of password resets are different things,
// and summed they would let a steady trickle of one hide behind the other.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public static class DashboardSecuritySpike
{
    public const int MinimumLast24Hours = 5;

    public const int SpikeFactor = 3;

    public static bool IsSpiking(int last24Hours, int last7Days, DateTimeOffset? countedSince, DateTimeOffset asOf)
    {
        if (last24Hours < MinimumLast24Hours || countedSince is null)
        {
            return false;
        }

        var dayStart = asOf.AddHours(-24);
        var baselineStart = countedSince.Value > asOf.AddDays(-7) ? countedSince.Value : asOf.AddDays(-7);
        var baselineDays = (dayStart - baselineStart).TotalDays;

        if (baselineDays < 1)
        {
            return false;
        }

        var dailyAverage = (last7Days - last24Hours) / baselineDays;
        return last24Hours >= SpikeFactor * dailyAverage;
    }
}
