using System.Diagnostics;

namespace Amortizer.Tests;

/// <summary>Thousands of random loans: whatever the terms, a schedule must add up exactly.</summary>
public class InvariantTests
{
    [Fact]
    public void EveryRandomScheduleAddsUpToTheCent()
    {
        var rnd = new Random(20261104);
        var methods = Enum.GetValues<Method>();
        var freqs = Enum.GetValues<Frequency>();
        var counts = Enum.GetValues<DayCount>();
        var roundings = new[] { MidpointRounding.AwayFromZero, MidpointRounding.ToEven };
        for (var i = 0; i < 3000; i++)
        {
            var method = methods[rnd.Next(methods.Length)];
            var periods = rnd.Next(1, 400);
            var decimals = rnd.Next(0, 5);
            var principal = Math.Round((decimal)(rnd.NextDouble() * 1_000_000 + 1), decimals);
            if (principal <= 0) principal = 1;
            var grace = method is Method.Annuity or Method.EqualPrincipal && periods > 1 && rnd.Next(4) == 0 ? rnd.Next(0, periods) : 0;
            var extras = method is Method.Annuity or Method.EqualPrincipal && rnd.Next(3) == 0
                ? Enumerable.Range(0, rnd.Next(1, 4)).Select(_ => new ExtraPayment(rnd.Next(1, periods + 1), Math.Round((decimal)rnd.NextDouble() * principal / 3 + 1, decimals))).ToList()
                : [];
            var terms = new LoanTerms
            {
                Principal = principal,
                AnnualRatePercent = Math.Round((decimal)(rnd.NextDouble() * 60), 3),
                Periods = periods,
                Method = method,
                Frequency = freqs[rnd.Next(freqs.Length)],
                DayCount = counts[rnd.Next(counts.Length)],
                Decimals = decimals,
                Rounding = roundings[rnd.Next(2)],
                GracePeriods = grace,
                StartDate = new DateOnly(2020 + rnd.Next(10), rnd.Next(1, 13), rnd.Next(1, 29)),
                ExtraPayments = extras,
                UpfrontFees = rnd.Next(4) == 0 ? Math.Round(principal * 0.02m, decimals) : 0,
            };
            Check(terms);
        }
    }

    private static void Check(LoanTerms t)
    {
        var s = Loan.Build(t);
        var why = $"{t}";
        Assert.True(s.Rows.Count >= 1 && s.Rows.Count <= t.Periods, why);
        Assert.Equal(t.Principal, s.TotalPrincipal);
        Assert.Equal(0m, s.Rows[^1].ClosingBalance);
        var balance = t.Principal;
        var date = t.StartDate;
        foreach (var r in s.Rows)
        {
            Assert.Equal(balance, r.OpeningBalance);
            Assert.Equal(r.Interest + r.Principal, r.Payment);
            Assert.Equal(r.OpeningBalance - r.Principal, r.ClosingBalance);
            Assert.True(r.Interest >= 0 && r.Principal >= 0 && r.ClosingBalance >= 0, why);
            Assert.True(r.Date > date, why);
            foreach (var v in new[] { r.Interest, r.Principal, r.Payment })
                Assert.Equal(Math.Round(v, t.Decimals), v);
            balance = r.ClosingBalance;
            date = r.Date;
        }
        Assert.Equal(s.TotalPrincipal + s.TotalInterest, s.TotalPaid);
        if (s.Apr is decimal apr) Assert.True(apr >= 0, why);
    }

    [Theory]
    [InlineData(Method.Annuity)]
    [InlineData(Method.EqualPrincipal)]
    [InlineData(Method.InterestOnly)]
    [InlineData(Method.FlatRate)]
    public void TheLargestLoansAllowedFinishQuicklyWithoutOverflow(Method m)
    {
        foreach (var f in Enum.GetValues<Frequency>())
        {
            var t = new LoanTerms { Principal = LoanTerms.MaxPrincipal, AnnualRatePercent = LoanTerms.MaxRatePercent, Periods = LoanTerms.MaxPeriods, Method = m, Frequency = f, Decimals = 4 };
            var sw = Stopwatch.StartNew();
            var s = Loan.Build(t);
            Assert.True(sw.Elapsed.TotalSeconds < 5, $"{m} {f}: {sw.Elapsed.TotalSeconds:0.0} s");
            Assert.Equal(t.Principal, s.TotalPrincipal);
            // a rate this extreme may be too large to express, but must never be wrong or throw
            if (s.EffectiveAnnualRate is decimal ear) Assert.True(ear > 0);
        }
    }
}
