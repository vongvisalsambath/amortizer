namespace Amortizer.Tests;

public class CalendarTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 28, false, 28)]
    [InlineData(2026, 1, 30, 2026, 3, 31, false, 60)]  // start on the 30th: the 31st counts as the 30th
    [InlineData(2026, 1, 15, 2026, 3, 31, false, 76)]  // bond basis keeps the 31st when the start is before the 30th
    [InlineData(2026, 1, 15, 2026, 3, 31, true, 75)]   // Eurobond basis always counts it as the 30th
    [InlineData(2026, 1, 31, 2026, 7, 31, false, 180)]
    public void ThirtyThreeSixty(int y1, int m1, int d1, int y2, int m2, int d2, bool european, int expected) =>
        Assert.Equal(expected, Calendar.Thirty360(D(y1, m1, d1), D(y2, m2, d2), european));

    [Fact]
    public void ActualActualSplitsAcrossALeapYear()
    {
        var f = Calendar.YearFraction(D(2027, 12, 1), D(2028, 3, 1), DayCount.ActualActual);
        Assert.Equal(31m / 365 + 60m / 366, f);
    }

    [Fact]
    public void MonthlyDatesKeepTheirDay()
    {
        var anchor = D(2026, 1, 31);
        Assert.Equal(D(2026, 2, 28), Calendar.Step(anchor, Frequency.Monthly, 1));
        Assert.Equal(D(2026, 3, 31), Calendar.Step(anchor, Frequency.Monthly, 2)); // not the 28th
        Assert.Equal(D(2028, 2, 29), Calendar.Step(anchor, Frequency.Monthly, 25));
    }

    [Fact]
    public void BackwardsDatesAreRefused() =>
        Assert.Throws<ArgumentException>(() => Calendar.YearFraction(D(2026, 2, 1), D(2026, 1, 1), DayCount.Actual365Fixed));
}
