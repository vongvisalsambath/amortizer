namespace Amortizer.Tests;

public class ValidationTests
{
    private static LoanTerms Ok => new() { Principal = 1000m, AnnualRatePercent = 10m, Periods = 12 };

    public static IEnumerable<object[]> BadTerms() => new[]
    {
        new object[] { Ok with { Principal = 0 }, "more than zero" },
        [Ok with { Principal = -5 }, "more than zero"],
        [Ok with { Principal = LoanTerms.MaxPrincipal + 1 }, "at most"],
        [Ok with { AnnualRatePercent = -1 }, "negative"],
        [Ok with { AnnualRatePercent = 1000.01m }, "at most"],
        [Ok with { Periods = 0 }, "at least one"],
        [Ok with { Periods = LoanTerms.MaxPeriods + 1 }, "at most"],
        [Ok with { Periods = int.MaxValue }, "at most"],
        [Ok with { Decimals = 5 }, "decimal places"],
        [Ok with { Decimals = -1 }, "decimal places"],
        [Ok with { GracePeriods = 12 }, "shorter"],
        [Ok with { GracePeriods = -1 }, "negative"],
        [Ok with { GracePeriods = 2, Method = Method.FlatRate }, "grace period only"],
        [Ok with { UpfrontFees = 1000m }, "less than"],
        [Ok with { UpfrontFees = -1m }, "negative"],
        [Ok with { FirstPaymentDate = new DateOnly(2026, 1, 1) }, "after"],
        [Ok with { FirstPaymentDate = new DateOnly(2030, 1, 1) }, "two years"],
        [Ok with { StartDate = new DateOnly(9999, 1, 1) }, "future"],
        [Ok with { Frequency = (Frequency)99 }, "frequency"],
        [Ok with { Method = (Method)99 }, "method"],
        [Ok with { DayCount = (DayCount)99 }, "day-count"],
        [Ok with { Rounding = (MidpointRounding)99 }, "rounding"],
        [Ok with { ExtraPayments = [new(13, 10m)] }, "no such payment"],
        [Ok with { ExtraPayments = [new(1, 0m)] }, "more than zero"],
        [Ok with { ExtraPayments = [new(1, -3m)] }, "more than zero"],
        [Ok with { ExtraPayments = [null!] }, "missing"],
        [Ok with { ExtraPayments = null! }, "null"],
        [Ok with { ExtraPayments = [new(1, 10m)], Method = Method.InterestOnly }, "only apply"],
        [Ok with { ExtraPayments = Enumerable.Range(1, LoanTerms.MaxPeriods + 1).Select(i => new ExtraPayment(1, 1m)).ToList() }, "at most"],
    };

    [Theory]
    [MemberData(nameof(BadTerms))]
    public void BadTermsAreRefusedWithAReason(LoanTerms terms, string reason)
    {
        var ex = Assert.Throws<LoanTermsException>(() => Loan.Build(terms));
        Assert.Contains(ex.Problems, p => p.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullTermsAreRefused() => Assert.Throws<ArgumentNullException>(() => Loan.Build(null!));

    [Fact]
    public void GoodTermsHaveNoProblems() => Assert.Empty(Ok.Problems());
}
