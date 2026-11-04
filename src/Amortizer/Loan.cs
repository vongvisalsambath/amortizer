namespace Amortizer;

/// <summary>Builds repayment schedules.</summary>
public static class Loan
{
    /// <summary>
    /// The full schedule for a loan. Every amount is rounded to <see cref="LoanTerms.Decimals"/> places, the last payment
    /// absorbs the rounding, and the principal repaid always adds up to exactly the amount borrowed.
    /// </summary>
    /// <exception cref="LoanTermsException">The terms don't make sense; <see cref="LoanTermsException.Problems"/> says why.</exception>
    public static Schedule Build(LoanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var problems = terms.Problems();
        if (problems.Count > 0) throw new LoanTermsException(problems);

        var rows = terms.Method switch
        {
            Method.Annuity => Amortizing(terms, equalPrincipal: false),
            Method.EqualPrincipal => Amortizing(terms, equalPrincipal: true),
            Method.InterestOnly => InterestOnly(terms),
            Method.FlatRate => Flat(terms),
            _ => throw new LoanTermsException(["Unknown loan method."]),
        };
        var rate = Maths.InternalRate(terms.Principal - terms.UpfrontFees, rows.Select(r => r.Payment).ToList());
        return new Schedule(terms, rows, rate);
    }

    /// <summary>The usual payment for an annuity: P·r / (1 − (1 + r)^−n), rounded; P / n when the rate is zero.</summary>
    public static decimal AnnuityPayment(decimal principal, decimal annualRatePercent, int periods, Frequency frequency, int decimals = 2, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        if (periods < 1) throw new ArgumentOutOfRangeException(nameof(periods));
        var r = annualRatePercent / 100m / Calendar.PerYear(frequency);
        decimal pmt;
        if (r == 0) pmt = principal / periods;
        else
        {
            var vn = Maths.Discount(1 / (1 + r), periods); // (1 + r)^-n, which can't overflow
            pmt = principal * r / (1 - vn);
        }
        return Math.Round(pmt, decimals, rounding);
    }

    private static decimal R(LoanTerms t, decimal x) => Math.Round(x, t.Decimals, t.Rounding);

    /// <summary>The payment dates, and the fraction of a year each period's interest covers.</summary>
    private static (DateOnly Date, int Days, decimal Years)[] Periods(LoanTerms t)
    {
        var ppy = Calendar.PerYear(t.Frequency);
        var first = t.FirstPaymentDate ?? Calendar.Step(t.StartDate, t.Frequency, 1);
        var regularFirstDays = Calendar.Days(t.StartDate, Calendar.Step(t.StartDate, t.Frequency, 1));
        var result = new (DateOnly, int, decimal)[t.Periods];
        var prev = t.StartDate;
        for (var k = 0; k < t.Periods; k++)
        {
            var date = Calendar.Step(first, t.Frequency, k);
            var days = Calendar.Days(prev, date);
            decimal years;
            if (t.DayCount == DayCount.Periodic)
                // a regular period is exactly 1 / ppy; an odd first period is scaled by its actual length
                years = k == 0 && t.FirstPaymentDate is not null ? (decimal)days / regularFirstDays / ppy : 1m / ppy;
            else
                years = Calendar.YearFraction(prev, date, t.DayCount);
            result[k] = (date, days, years);
            prev = date;
        }
        return result;
    }

    private static List<Row> Amortizing(LoanTerms t, bool equalPrincipal)
    {
        var periods = Periods(t);
        var rate = t.AnnualRatePercent / 100m;
        var extras = t.ExtraPayments.GroupBy(x => x.Period).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var repaying = t.Periods - t.GracePeriods;
        var payment = AnnuityPayment(t.Principal, t.AnnualRatePercent, repaying, t.Frequency, t.Decimals, t.Rounding);
        var slice = R(t, t.Principal / repaying);
        var rows = new List<Row>(t.Periods);
        var balance = t.Principal;
        for (var k = 0; k < t.Periods && balance > 0; k++)
        {
            var (date, days, years) = periods[k];
            var n = k + 1;
            var interest = R(t, balance * rate * years);
            decimal principal;
            if (n <= t.GracePeriods) principal = 0;
            else if (n == t.Periods) principal = balance;                           // the last payment clears the balance
            else if (equalPrincipal) principal = Math.Min(slice, balance);
            else principal = Math.Clamp(payment - interest, 0, balance);            // never negative, never past zero
            var extra = 0m;
            if (extras.TryGetValue(n, out var x)) extra = Math.Min(x, balance - principal);
            principal += extra;
            var closing = balance - principal;
            rows.Add(new Row(n, date, days, balance, interest, principal, extra, interest + principal, closing));
            balance = closing;
        }
        return rows;
    }

    private static List<Row> InterestOnly(LoanTerms t)
    {
        var periods = Periods(t);
        var rate = t.AnnualRatePercent / 100m;
        var rows = new List<Row>(t.Periods);
        for (var k = 0; k < t.Periods; k++)
        {
            var (date, days, years) = periods[k];
            var interest = R(t, t.Principal * rate * years);
            var principal = k == t.Periods - 1 ? t.Principal : 0;
            rows.Add(new Row(k + 1, date, days, t.Principal, interest, principal, 0, interest + principal, t.Principal - principal));
        }
        return rows;
    }

    private static List<Row> Flat(LoanTerms t)
    {
        // Interest on the original amount for the whole term, spread evenly. The day count doesn't apply: the term is
        // payments / payments per year, which is how flat rates are quoted.
        var periods = Periods(t);
        var totalInterest = R(t, t.Principal * t.AnnualRatePercent / 100m * t.Periods / Calendar.PerYear(t.Frequency));
        var interestSlice = R(t, totalInterest / t.Periods);
        var principalSlice = R(t, t.Principal / t.Periods);
        var rows = new List<Row>(t.Periods);
        decimal balance = t.Principal, interestLeft = totalInterest;
        for (var k = 0; k < t.Periods; k++)
        {
            var (date, days, _) = periods[k];
            var last = k == t.Periods - 1;
            var interest = last ? interestLeft : Math.Min(interestSlice, interestLeft);
            var principal = last ? balance : Math.Min(principalSlice, balance);
            rows.Add(new Row(k + 1, date, days, balance, interest, principal, 0, interest + principal, balance - principal));
            balance -= principal;
            interestLeft -= interest;
        }
        return rows;
    }
}
