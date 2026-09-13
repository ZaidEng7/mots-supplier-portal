// When an onboarding review is due, counted in working days.
//
// Working days rather than calendar days, because the target a ministry states is a working-day
// commitment, and because a case submitted on a Thursday would otherwise be late before anyone
// was at their desk.
//
// Friday and Saturday are the weekend. That is the Syrian working week, and it is the only
// assumption in here. Public holidays are deliberately not modelled: no document lists them, a
// hard-coded calendar would be wrong within a year, and a target that is a day optimistic is a
// far smaller error than one computed against the wrong country's week.
//
// TargetFor walks forward one day at a time from the moment the case entered the queue, counting
// only the days that are not weekend, until it has counted the number asked for.

namespace MotsSupplierPortal.Domain.Configuration;

public static class ReviewSla
{
    private static bool IsWeekend(DateTimeOffset moment) =>
        moment.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    public static DateTimeOffset TargetFor(DateTimeOffset enteredQueueAt, int workingDays)
    {
        var target = enteredQueueAt;
        var remaining = workingDays;

        while (remaining > 0)
        {
            target = target.AddDays(1);
            if (!IsWeekend(target)) remaining--;
        }

        return target;
    }
}
