using System.Diagnostics;

namespace Amortizer.Tests;

public class ScheduleTests
{
    private static LoanTerms Terms(decimal p, decimal rate, int n, Method m = Method.Annuity) =>
        new() { Principal = p, AnnualRatePercent = rate, Periods = n, Method = m };

    [Fact]
    public void AThirtyYearMortgageMatchesThePublishedPayment()
    {
        // $200,000 at 6% for 30 years: the textbook monthly payment is $1,199.10
        var s = Loan.Build(Terms(200_000m, 6m, 360));
        Assert.Equal(1199.10m, s.RegularPayment);
        Assert.Equal(200_000m, s.TotalPrincipal);
        Assert.Equal(0m, s.Rows[^1].ClosingBalance);
        Assert.Equal(360, s.Rows.Count);
        Assert.Equal(new DateOnly(2056, 1, 1), s.PayoffDate);
    }

    [Fact]
    public void ScheduleMatchesAnIndependentSimulation()
    {
        // the same loan computed separately in floating point, rounding each step the same way
        var s = Loan.Build(Terms(10_000m, 12m, 12));
        Assert.Equal(888.49m, s.RegularPayment);
        double bal = 10_000, totalInterest = 0;
        for (var k = 0; k < 12; k++)
        {
            var interest = Math.Round(bal * 0.01, 2, MidpointRounding.AwayFromZero);
            var principal = k == 11 ? bal : 888.49 - interest;
            totalInterest += interest;
            bal = Math.Round(bal - principal, 2);
            Assert.Equal((decimal)interest, s.Rows[k].Interest);
        }
        Assert.Equal(Math.Round(totalInterest, 2), (double)s.TotalInterest, 2);
    }

    [Fact]
    public void ZeroInterestSplitsThePrincipalEvenly()
    {
        var s = Loan.Build(Terms(1200m, 0m, 12));
        Assert.All(s.Rows, r => Assert.Equal(100m, r.Payment));
        Assert.Equal(0m, s.TotalInterest);
        Assert.Equal(0m, s.Apr);
    }

    [Fact]
    public void EqualPrincipalPaymentsFall()
    {
        var s = Loan.Build(Terms(12_000m, 12m, 12, Method.EqualPrincipal));
        Assert.All(s.Rows, r => Assert.Equal(1000m, r.Principal));
        Assert.Equal(120m, s.Rows[0].Interest);
        Assert.Equal(10m, s.Rows[^1].Interest);
        Assert.True(s.Rows.Zip(s.Rows.Skip(1)).All(p => p.First.Payment > p.Second.Payment));
    }

    [Fact]
    public void InterestOnlyEndsWithTheBalloon()
    {
        var s = Loan.Build(Terms(10_000m, 12m, 12, Method.InterestOnly));
        Assert.All(s.Rows.Take(11), r => Assert.Equal(100m, r.Payment));
        Assert.Equal(10_100m, s.Rows[^1].Payment);
    }

    [Fact]
    public void AFlatRateCostsFarMoreThanItsQuotedRate()
    {
        // "1.5% a month flat" on $1,000 for a year: $180 interest, $98.33 a month
        var s = Loan.Build(Terms(1000m, 18m, 12, Method.FlatRate));
        Assert.Equal(180m, s.TotalInterest);
        Assert.Equal(98.33m, s.RegularPayment);
        Assert.Equal(1000m, s.TotalPrincipal);
        // the true annual rate is about 32%, checked with an independent solver
        var apr = IndependentApr(1000, s.Rows.Select(r => (double)r.Payment).ToArray(), 12);
        Assert.InRange((double)s.Apr!.Value, apr - 0.001, apr + 0.001);
        Assert.InRange(s.Apr!.Value, 31.5m, 32.5m);
        Assert.True(s.EffectiveAnnualRate > s.Apr);
    }

    [Fact]
    public void APlainAnnuitysAprIsItsQuotedRate()
    {
        var s = Loan.Build(Terms(200_000m, 6m, 360));
        Assert.InRange(s.Apr!.Value, 5.999m, 6.001m);
        Assert.InRange(s.EffectiveAnnualRate!.Value, 6.16m, 6.17m); // (1.005)^12 − 1 = 6.1678%
    }

    [Fact]
    public void FeesRaiseTheApr()
    {
        var s = Loan.Build(Terms(10_000m, 12m, 12) with { UpfrontFees = 200m });
        Assert.InRange(s.Apr!.Value, 15.5m, 16.5m);
        Assert.Equal(Loan.Build(Terms(10_000m, 12m, 12)).TotalPaid, s.TotalPaid); // the schedule itself doesn't change
    }

    [Fact]
    public void GracePeriodsAreInterestOnly()
    {
        var s = Loan.Build(Terms(12_000m, 12m, 12) with { GracePeriods = 3 });
        Assert.All(s.Rows.Take(3), r => { Assert.Equal(120m, r.Interest); Assert.Equal(0m, r.Principal); });
        Assert.Equal(12_000m, s.TotalPrincipal);
        Assert.Equal(Loan.AnnuityPayment(12_000m, 12m, 9, Frequency.Monthly), s.RegularPayment);
    }

    [Fact]
    public void ExtraPaymentsShortenTheLoan()
    {
        var plain = Loan.Build(Terms(10_000m, 12m, 24));
        var extra = Loan.Build(Terms(10_000m, 12m, 24) with { ExtraPayments = [new(1, 3000m), new(5, 2000m)] });
        Assert.True(extra.Rows.Count < plain.Rows.Count);
        Assert.True(extra.TotalInterest < plain.TotalInterest);
        Assert.Equal(10_000m, extra.TotalPrincipal);
        Assert.Equal(0m, extra.Rows[^1].ClosingBalance);
        Assert.Equal(3000m, extra.Rows[0].Extra);
    }

    [Fact]
    public void AnExtraPaymentLargerThanTheBalanceStopsAtZero()
    {
        var s = Loan.Build(Terms(1000m, 12m, 12) with { ExtraPayments = [new(2, 5000m)] });
        Assert.Equal(2, s.Rows.Count);
        Assert.Equal(0m, s.Rows[^1].ClosingBalance);
        Assert.Equal(1000m, s.TotalPrincipal);
    }

    [Fact]
    public void RielLoansRoundToWholeRiel()
    {
        var s = Loan.Build(Terms(4_000_000m, 18m, 12) with { Decimals = 0 });
        Assert.All(s.Rows, r => { Assert.Equal(Math.Round(r.Payment), r.Payment); Assert.Equal(Math.Round(r.Interest), r.Interest); });
        Assert.Equal(4_000_000m, s.TotalPrincipal);
    }

    [Fact]
    public void ActualDayCountsChargeByTheDay()
    {
        var a360 = Loan.Build(Terms(100_000m, 10m, 12) with { DayCount = DayCount.Actual360 });
        Assert.Equal(31, a360.Rows[0].Days);
        Assert.Equal(Math.Round(100_000m * 0.10m * 31 / 360, 2), a360.Rows[0].Interest);
        var a365 = Loan.Build(Terms(100_000m, 10m, 12) with { DayCount = DayCount.Actual365Fixed });
        Assert.True(a360.TotalInterest > a365.TotalInterest);
    }

    [Fact]
    public void AnOddFirstPeriodChargesItsExtraDays()
    {
        var regular = Loan.Build(Terms(10_000m, 12m, 12));
        var late = Loan.Build(Terms(10_000m, 12m, 12) with { FirstPaymentDate = new DateOnly(2026, 3, 1) });
        Assert.Equal(59, late.Rows[0].Days);
        Assert.True(late.Rows[0].Interest > regular.Rows[0].Interest * 1.8m);
    }

    private static double IndependentApr(double received, double[] payments, int ppy)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var npv = payments.Select((p, k) => p / Math.Pow(1 + mid, k + 1)).Sum() - received;
            if (npv > 0) lo = mid; else hi = mid;
        }
        return lo * ppy * 100;
    }
}
