namespace Amortizer;

/// <summary>Payment dates and year fractions.</summary>
public static class Calendar
{
    /// <summary>Payments per year for a frequency.</summary>
    public static int PerYear(Frequency f) => f switch
    {
        Frequency.Weekly => 52,
        Frequency.Biweekly => 26,
        Frequency.Monthly => 12,
        Frequency.Quarterly => 4,
        Frequency.SemiAnnual => 2,
        Frequency.Annual => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    /// <summary>
    /// The date <paramref name="count"/> periods after <paramref name="anchor"/>. Month-based steps are counted from the
    /// anchor every time, so a loan paid on the 31st is paid on the 31st again after a short month (on the 30th or 28th
    /// in between), instead of drifting earlier.
    /// </summary>
    public static DateOnly Step(DateOnly anchor, Frequency f, int count) => f switch
    {
        Frequency.Weekly => anchor.AddDays(7 * count),
        Frequency.Biweekly => anchor.AddDays(14 * count),
        Frequency.Monthly => anchor.AddMonths(count),
        Frequency.Quarterly => anchor.AddMonths(3 * count),
        Frequency.SemiAnnual => anchor.AddMonths(6 * count),
        Frequency.Annual => anchor.AddMonths(12 * count),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    /// <summary>The fraction of a year from <paramref name="from"/> to <paramref name="to"/> under a day-count convention.</summary>
    public static decimal YearFraction(DateOnly from, DateOnly to, DayCount dc)
    {
        if (to < from) throw new ArgumentException("The end date is before the start date.");
        return dc switch
        {
            DayCount.Thirty360 => Thirty360(from, to, european: false) / 360m,
            DayCount.Thirty360European => Thirty360(from, to, european: true) / 360m,
            DayCount.Actual360 => Days(from, to) / 360m,
            DayCount.Actual365Fixed => Days(from, to) / 365m,
            DayCount.ActualActual => ActualActual(from, to),
            _ => throw new ArgumentException("Periodic interest has no year fraction of its own; it is 1 / payments per year.", nameof(dc)),
        };
    }

    /// <summary>Calendar days between two dates.</summary>
    public static int Days(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    /// <summary>
    /// Days between two dates counting every month as 30 days. Bond basis (ISDA 2006 4.16(f)): a 31st start counts as the
    /// 30th, and a 31st end counts as the 30th only when the start is the 30th or 31st. Eurobond basis (4.16(g)): every 31st
    /// counts as the 30th.
    /// </summary>
    public static int Thirty360(DateOnly from, DateOnly to, bool european)
    {
        int d1 = from.Day, d2 = to.Day;
        if (european)
        {
            d1 = Math.Min(d1, 30);
            d2 = Math.Min(d2, 30);
        }
        else
        {
            d1 = Math.Min(d1, 30);
            if (d1 >= 30) d2 = Math.Min(d2, 30);
        }
        return 360 * (to.Year - from.Year) + 30 * (to.Month - from.Month) + (d2 - d1);
    }

    private static decimal ActualActual(DateOnly from, DateOnly to)
    {
        // each calendar year's days over that year's length
        decimal total = 0;
        var cur = from;
        while (cur < to)
        {
            var nextYear = new DateOnly(cur.Year + 1, 1, 1);
            var end = to < nextYear ? to : nextYear;
            total += (decimal)Days(cur, end) / (DateTime.IsLeapYear(cur.Year) ? 366 : 365);
            cur = end;
        }
        return total;
    }
}
