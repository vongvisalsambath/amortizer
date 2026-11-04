namespace Amortizer;

/// <summary>Decimal helpers: integer powers and the rate solver.</summary>
internal static class Maths
{
    /// <summary>x^n for a whole n ≥ 0, by repeated squaring. Throws OverflowException when the result is too large.</summary>
    public static decimal Pow(decimal x, int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        decimal result = 1, b = x;
        while (n > 0)
        {
            if ((n & 1) == 1) result *= b;
            n >>= 1;
            if (n > 0) b *= b;
        }
        return result;
    }

    /// <summary>
    /// v^n for 0 &lt; v ≤ 1 (a discount factor). It can only shrink, so it never overflows; very small results become 0.
    /// </summary>
    public static decimal Discount(decimal v, int n)
    {
        decimal result = 1, b = v;
        while (n > 0)
        {
            if ((n & 1) == 1) result *= b;
            n >>= 1;
            if (n > 0) b *= b;
            if (result == 0) break;
        }
        return result;
    }

    /// <summary>
    /// The rate per period r ≥ 0 at which the payments, discounted, equal what was received:
    /// received = Σ payment_k / (1 + r)^k. Bisection in decimal arithmetic, so it is exact to about 1e-14.
    /// Null when the rate is above 1000% a period.
    /// </summary>
    public static decimal? InternalRate(decimal received, IReadOnlyList<decimal> payments)
    {
        decimal Npv(decimal r)
        {
            decimal v = 1 / (1 + r), f = 1, sum = 0;
            foreach (var p in payments)
            {
                f *= v;
                sum += p * f;
            }
            return sum - received; // falls as r rises
        }
        var total = payments.Sum();
        if (total <= received) return 0; // nothing paid for the money
        decimal lo = 0, hi = 10;
        if (Npv(hi) > 0) return null;
        for (var i = 0; i < 200 && hi - lo > 0.000000000000001m; i++)
        {
            var mid = (lo + hi) / 2;
            if (Npv(mid) > 0) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
