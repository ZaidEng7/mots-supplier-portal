namespace MotsSupplierPortal.Domain.Configuration;

/// <summary>
/// A-5: when an onboarding review is DUE, counted in working days.
///
/// <para>Working days, not calendar days, because the SLA a ministry states is a working-day
/// commitment - and because a case submitted on a Thursday would otherwise be "late" before anyone
/// was at their desk.</para>
///
/// <para><b>Friday and Saturday are the weekend.</b> That is the Syrian working week, and it is the
/// only assumption in here. Public holidays are NOT modelled: no document lists them, a hard-coded
/// calendar would be wrong within a year, and a target that is a day optimistic is a far smaller
/// error than a target computed against the wrong country's week. Recorded rather than hidden.</para>
/// </summary>
public static class ReviewSla
{
    private static bool IsWeekend(DateTimeOffset moment) =>
        moment.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    /// <summary>The target date for a case that entered the queue at <paramref name="enteredQueueAt"/>.</summary>
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
