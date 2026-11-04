namespace Amortizer;

/// <summary>How each payment is made up.</summary>
public enum Method
{
    /// <summary>Equal payments (an annuity, as in most mortgages and car loans). Early payments are mostly interest.</summary>
    Annuity,
    /// <summary>The same principal every period plus interest on what is left, so payments fall over time (declining balance).</summary>
    EqualPrincipal,
    /// <summary>Interest only each period, and the whole principal with the last payment (a balloon).</summary>
    InterestOnly,
    /// <summary>
    /// Interest worked out once on the original amount for the whole term and spread evenly, as many microfinance and
    /// consumer loans quote it ("1.5% a month flat"). The true cost is far higher than the quoted rate suggests: see
    /// <see cref="Schedule.EffectiveAnnualRate"/>.
    /// </summary>
    FlatRate,
}

/// <summary>How often payments are made.</summary>
public enum Frequency
{
    /// <summary>Every 7 days.</summary>
    Weekly,
    /// <summary>Every 14 days.</summary>
    Biweekly,
    /// <summary>Every month, on the same day of the month (the last day when a month is shorter).</summary>
    Monthly,
    /// <summary>Every 3 months.</summary>
    Quarterly,
    /// <summary>Every 6 months.</summary>
    SemiAnnual,
    /// <summary>Every 12 months.</summary>
    Annual,
}

/// <summary>How the interest for a period is measured.</summary>
public enum DayCount
{
    /// <summary>Each period is exactly 1/12 of a year (monthly), 1/52 (weekly) and so on, whatever the calendar says. Most consumer loans and mortgages.</summary>
    Periodic,
    /// <summary>30/360 bond basis (ISDA): every month counts as 30 days, the year as 360.</summary>
    Thirty360,
    /// <summary>30E/360 (Eurobond basis): like 30/360, but a 31st always counts as the 30th.</summary>
    Thirty360European,
    /// <summary>Actual days in the period, over a 360-day year. Common for commercial loans; it charges slightly more than ACT/365.</summary>
    Actual360,
    /// <summary>Actual days in the period, over a 365-day year (also in leap years).</summary>
    Actual365Fixed,
    /// <summary>Actual days, each year's share over that year's length (365 or 366): ACT/ACT ISDA.</summary>
    ActualActual,
}

/// <summary>An extra payment of principal with a regular payment.</summary>
/// <param name="Period">The payment number it is made with (1 is the first payment).</param>
/// <param name="Amount">The extra principal paid. It never takes the balance below zero.</param>
public sealed record ExtraPayment(int Period, decimal Amount);

/// <summary>Everything that defines a loan.</summary>
public sealed record LoanTerms
{
    /// <summary>The amount borrowed.</summary>
    public required decimal Principal { get; init; }

    /// <summary>
    /// The quoted yearly interest rate in percent: 12 means 12% a year. For <see cref="Method.FlatRate"/> it is the flat rate
    /// (a flat 1.5% a month is 18).
    /// </summary>
    public required decimal AnnualRatePercent { get; init; }

    /// <summary>The number of payments.</summary>
    public required int Periods { get; init; }

    /// <summary>How often payments are made. Default monthly.</summary>
    public Frequency Frequency { get; init; } = Frequency.Monthly;

    /// <summary>How each payment is made up. Default <see cref="Method.Annuity"/>.</summary>
    public Method Method { get; init; } = Method.Annuity;

    /// <summary>How interest is measured per period. Default <see cref="DayCount.Periodic"/>.</summary>
    public DayCount DayCount { get; init; } = DayCount.Periodic;

    /// <summary>The day the money is paid out. Default 1 January 2026, so schedules are reproducible.</summary>
    public DateOnly StartDate { get; init; } = new(2026, 1, 1);

    /// <summary>The first payment date. Default one period after <see cref="StartDate"/>.</summary>
    public DateOnly? FirstPaymentDate { get; init; }

    /// <summary>Decimal places money is rounded to: 2 for USD, 0 for KHR or JPY. Default 2.</summary>
    public int Decimals { get; init; } = 2;

    /// <summary>How amounts are rounded. Default away from zero (the usual commercial rounding: 0.005 becomes 0.01).</summary>
    public MidpointRounding Rounding { get; init; } = MidpointRounding.AwayFromZero;

    /// <summary>Payments at the start that are interest only (a grace period). Default 0.</summary>
    public int GracePeriods { get; init; }

    /// <summary>Fees paid at the start, taken out of the money received. They raise the true cost (APR) without changing the schedule.</summary>
    public decimal UpfrontFees { get; init; }

    /// <summary>Extra principal payments.</summary>
    public IReadOnlyList<ExtraPayment> ExtraPayments { get; init; } = [];

    /// <summary>Largest principal accepted (10^15): beyond it decimal arithmetic could overflow over a long term.</summary>
    public const decimal MaxPrincipal = 1_000_000_000_000_000m;
    /// <summary>Highest yearly rate accepted, in percent.</summary>
    public const decimal MaxRatePercent = 1000m;
    /// <summary>Most payments accepted: 100 years of weekly payments fit with room to spare.</summary>
    public const int MaxPeriods = 6000;

    /// <summary>Every problem with these terms, in plain English. Empty when they're fine.</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (Principal <= 0) p.Add("The amount borrowed must be more than zero.");
        else if (Principal > MaxPrincipal) p.Add($"The amount borrowed can be at most {MaxPrincipal:N0}.");
        if (AnnualRatePercent < 0) p.Add("The interest rate can't be negative.");
        else if (AnnualRatePercent > MaxRatePercent) p.Add($"The interest rate can be at most {MaxRatePercent}% a year.");
        if (Periods < 1) p.Add("There must be at least one payment.");
        else if (Periods > MaxPeriods) p.Add($"There can be at most {MaxPeriods:N0} payments.");
        if (!Enum.IsDefined(Frequency)) p.Add("Unknown payment frequency.");
        if (!Enum.IsDefined(Method)) p.Add("Unknown loan method.");
        if (!Enum.IsDefined(DayCount)) p.Add("Unknown day-count convention.");
        if (!Enum.IsDefined(Rounding)) p.Add("Unknown rounding mode.");
        if (Decimals is < 0 or > 4) p.Add("Money can be rounded to 0 to 4 decimal places.");
        if (GracePeriods < 0) p.Add("The grace period can't be negative.");
        else if (Periods >= 1 && GracePeriods >= Periods) p.Add("The grace period must be shorter than the loan, or nothing is ever repaid until the end.");
        if (GracePeriods > 0 && Method is Method.InterestOnly or Method.FlatRate) p.Add("A grace period only applies to annuity and equal-principal loans.");
        if (UpfrontFees < 0) p.Add("Fees can't be negative.");
        else if (Principal > 0 && UpfrontFees >= Principal) p.Add("Fees must be less than the amount borrowed.");
        if (FirstPaymentDate is DateOnly first && first <= StartDate) p.Add("The first payment must come after the money is paid out.");
        if (FirstPaymentDate is DateOnly f2 && f2 > StartDate.AddYears(2)) p.Add("The first payment can be at most two years after the start.");
        if (StartDate.Year > 9000) p.Add("The start date is too far in the future.");
        if (ExtraPayments is null) p.Add("ExtraPayments can't be null.");
        else
        {
            if (ExtraPayments.Count > MaxPeriods) p.Add($"There can be at most {MaxPeriods:N0} extra payments.");
            foreach (var x in ExtraPayments.Take(MaxPeriods))
            {
                if (x is null) { p.Add("An extra payment is missing."); break; }
                if (x.Period < 1 || x.Period > Periods) { p.Add($"Extra payment for payment {x.Period}: there is no such payment."); break; }
                if (x.Amount <= 0 || x.Amount > MaxPrincipal) { p.Add($"Extra payment for payment {x.Period}: the amount must be more than zero and at most {MaxPrincipal:N0}."); break; }
            }
            if (ExtraPayments.Count > 0 && Method is Method.FlatRate or Method.InterestOnly)
                p.Add("Extra payments only apply to annuity and equal-principal loans.");
        }
        return p;
    }
}

/// <summary>Thrown when loan terms don't make sense. <see cref="Problems"/> lists every reason.</summary>
public sealed class LoanTermsException(IReadOnlyList<string> problems)
    : ArgumentException("The loan terms aren't valid: " + string.Join(" ", problems))
{
    /// <summary>Every problem found, in plain English.</summary>
    public IReadOnlyList<string> Problems { get; } = problems;
}
