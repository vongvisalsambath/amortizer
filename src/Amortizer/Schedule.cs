namespace Amortizer;

/// <summary>One payment in a schedule.</summary>
/// <param name="Number">The payment number, from 1.</param>
/// <param name="Date">When it is due.</param>
/// <param name="Days">Calendar days since the previous payment (or the start).</param>
/// <param name="OpeningBalance">What is owed before this payment.</param>
/// <param name="Interest">The interest part.</param>
/// <param name="Principal">The part that reduces the balance, the extra payment included.</param>
/// <param name="Extra">The extra principal paid with this payment, if any.</param>
/// <param name="Payment">The total paid: interest plus principal.</param>
/// <param name="ClosingBalance">What is owed after this payment.</param>
public sealed record Row(int Number, DateOnly Date, int Days, decimal OpeningBalance, decimal Interest, decimal Principal, decimal Extra, decimal Payment, decimal ClosingBalance);

/// <summary>A complete repayment schedule and what the loan really costs.</summary>
public sealed class Schedule
{
    internal Schedule(LoanTerms terms, IReadOnlyList<Row> rows, decimal? periodicRate)
    {
        Terms = terms;
        Rows = rows;
        TotalInterest = rows.Sum(r => r.Interest);
        TotalPrincipal = rows.Sum(r => r.Principal);
        TotalPaid = rows.Sum(r => r.Payment);
        PeriodicRate = periodicRate;
        var ppy = Calendar.PerYear(terms.Frequency);
        if (periodicRate is decimal r)
        {
            Apr = r * ppy * 100m;
            EffectiveAnnualRate = TryEffective(r, ppy);
        }
    }

    /// <summary>The terms this schedule was built from.</summary>
    public LoanTerms Terms { get; }

    /// <summary>Every payment, in order. A loan paid off early by extra payments has fewer rows than <see cref="LoanTerms.Periods"/>.</summary>
    public IReadOnlyList<Row> Rows { get; }

    /// <summary>All the interest paid over the loan.</summary>
    public decimal TotalInterest { get; }

    /// <summary>All the principal repaid: always exactly the amount borrowed.</summary>
    public decimal TotalPrincipal { get; }

    /// <summary>Everything paid: principal plus interest (fees are separate).</summary>
    public decimal TotalPaid { get; }

    /// <summary>The usual payment: the first one after any grace period.</summary>
    public decimal RegularPayment => Rows[Math.Min(Terms.GracePeriods, Rows.Count - 1)].Payment;

    /// <summary>The day the loan is paid off.</summary>
    public DateOnly PayoffDate => Rows[^1].Date;

    /// <summary>
    /// The interest rate per period that makes what you receive (the principal less fees) equal to what you pay back:
    /// the loan's internal rate of return. Null when it is too high to compute (over 1000% a period).
    /// </summary>
    public decimal? PeriodicRate { get; }

    /// <summary>
    /// The annual percentage rate, in percent: <see cref="PeriodicRate"/> × payments per year. It includes fees, and for a
    /// flat-rate loan it shows the real cost behind the quoted rate. Periods are treated as equal, as the US Truth in
    /// Lending actuarial method does for regular schedules.
    /// </summary>
    public decimal? Apr { get; }

    /// <summary>
    /// The effective annual rate, in percent: what the loan costs in a year once interest compounds,
    /// (1 + <see cref="PeriodicRate"/>)^payments per year − 1. Null when it is too large to represent.
    /// </summary>
    public decimal? EffectiveAnnualRate { get; }

    private static decimal? TryEffective(decimal r, int ppy)
    {
        try { return (Maths.Pow(1 + r, ppy) - 1) * 100m; }
        catch (OverflowException) { return null; }
    }
}
