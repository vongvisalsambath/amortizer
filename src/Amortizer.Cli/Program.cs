// amortizer: loan schedules from the command line.
using System.Globalization;
using System.Text.Json;
using Amortizer;

var inv = CultureInfo.InvariantCulture;
try
{
    return Run(args);
}
catch (LoanTermsException e)
{
    foreach (var p in e.Problems) Console.Error.WriteLine($"error: {p}");
    return 2;
}
catch (UsageException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    Console.Error.WriteLine("Run 'amortizer --help' for the options.");
    return 2;
}

int Run(string[] a)
{
    if (a.Length == 0 || a[0] is "-h" or "--help" or "help") return Help();
    var opts = Parse(a);
    var terms = new LoanTerms
    {
        Principal = Dec(opts, "amount", required: true)!.Value,
        AnnualRatePercent = Dec(opts, "rate", required: true)!.Value,
        Periods = Int(opts, "periods", required: true)!.Value,
        Frequency = Pick(opts, "frequency", Frequency.Monthly, new() { ["weekly"] = Frequency.Weekly, ["biweekly"] = Frequency.Biweekly, ["monthly"] = Frequency.Monthly, ["quarterly"] = Frequency.Quarterly, ["semiannual"] = Frequency.SemiAnnual, ["annual"] = Frequency.Annual }),
        Method = Pick(opts, "method", Method.Annuity, new() { ["annuity"] = Method.Annuity, ["equal-principal"] = Method.EqualPrincipal, ["interest-only"] = Method.InterestOnly, ["flat"] = Method.FlatRate }),
        DayCount = Pick(opts, "day-count", DayCount.Periodic, new() { ["periodic"] = DayCount.Periodic, ["30/360"] = DayCount.Thirty360, ["30e/360"] = DayCount.Thirty360European, ["act/360"] = DayCount.Actual360, ["act/365"] = DayCount.Actual365Fixed, ["act/act"] = DayCount.ActualActual }),
        Decimals = Int(opts, "decimals") ?? 2,
        StartDate = Date(opts, "start") ?? new DateOnly(2026, 1, 1),
        FirstPaymentDate = Date(opts, "first-payment"),
        GracePeriods = Int(opts, "grace") ?? 0,
        UpfrontFees = Dec(opts, "fees") ?? 0,
        ExtraPayments = Extras(opts),
    };
    var s = Loan.Build(terms);
    var format = opts.GetValueOrDefault("format") ?? "text";
    switch (format)
    {
        case "csv": Console.Write(s.ToCsv()); break;
        case "json":
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                payment = s.RegularPayment, totalInterest = s.TotalInterest, totalPaid = s.TotalPaid, apr = s.Apr, effectiveAnnualRate = s.EffectiveAnnualRate,
                payoffDate = s.PayoffDate, rows = s.Rows,
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            break;
        case "text":
            Console.WriteLine(string.Create(inv, $"Payment        {s.RegularPayment:N2}"));
            Console.WriteLine(string.Create(inv, $"Total interest {s.TotalInterest:N2}"));
            Console.WriteLine(string.Create(inv, $"Total paid     {s.TotalPaid:N2}"));
            Console.WriteLine(s.Apr is decimal apr ? string.Create(inv, $"APR            {apr:0.###}%") : "APR            too high to compute");
            Console.WriteLine(s.EffectiveAnnualRate is decimal ear ? string.Create(inv, $"Effective rate {ear:0.###}% a year") : "Effective rate too high to show");
            Console.WriteLine(string.Create(inv, $"Paid off       {s.PayoffDate:yyyy-MM-dd} ({s.Rows.Count} payments)"));
            if (opts.ContainsKey("schedule"))
            {
                Console.WriteLine();
                Console.WriteLine("   #  date          interest     principal       payment       balance");
                foreach (var r in s.Rows)
                    Console.WriteLine(string.Create(inv, $"{r.Number,4}  {r.Date:yyyy-MM-dd} {r.Interest,13:N2} {r.Principal,13:N2} {r.Payment,13:N2} {r.ClosingBalance,13:N2}"));
            }
            break;
        default: throw new UsageException($"Unknown format '{format}': use text, csv or json.");
    }
    return 0;
}

Dictionary<string, string?> Parse(string[] a)
{
    var flags = new HashSet<string> { "schedule" };
    var known = new HashSet<string> { "amount", "rate", "periods", "frequency", "method", "day-count", "decimals", "start", "first-payment", "grace", "fees", "extra", "format", "schedule" };
    var d = new Dictionary<string, string?>();
    for (var i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"Unexpected '{a[i]}'.");
        var key = a[i][2..];
        if (!known.Contains(key)) throw new UsageException($"Unknown option '--{key}'.");
        if (d.ContainsKey(key) && key != "extra") throw new UsageException($"'--{key}' is given twice.");
        if (flags.Contains(key)) { d[key] = null; continue; }
        if (i + 1 >= a.Length) throw new UsageException($"'--{key}' needs a value.");
        var v = a[++i];
        d[key] = key == "extra" && d.TryGetValue("extra", out var prev) ? prev + ";" + v : v;
    }
    return d;
}

decimal? Dec(Dictionary<string, string?> o, string k, bool required = false)
{
    if (!o.TryGetValue(k, out var v) || v is null) return required ? throw new UsageException($"'--{k}' is required.") : null;
    if (v.Length > 40 || !decimal.TryParse(v, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, inv, out var r))
        throw new UsageException($"'--{k}' must be a number like 1234.56, not '{Short(v)}'.");
    return r;
}

int? Int(Dictionary<string, string?> o, string k, bool required = false)
{
    if (!o.TryGetValue(k, out var v) || v is null) return required ? throw new UsageException($"'--{k}' is required.") : null;
    if (!int.TryParse(v, NumberStyles.AllowLeadingSign, inv, out var r)) throw new UsageException($"'--{k}' must be a whole number, not '{Short(v)}'.");
    return r;
}

DateOnly? Date(Dictionary<string, string?> o, string k)
{
    if (!o.TryGetValue(k, out var v) || v is null) return null;
    if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", inv, DateTimeStyles.None, out var r)) throw new UsageException($"'--{k}' must be a date like 2026-01-31, not '{Short(v)}'.");
    return r;
}

T Pick<T>(Dictionary<string, string?> o, string k, T fallback, Dictionary<string, T> choices)
{
    if (!o.TryGetValue(k, out var v) || v is null) return fallback;
    return choices.TryGetValue(v.ToLowerInvariant(), out var r) ? r : throw new UsageException($"'--{k}' must be one of {string.Join(", ", choices.Keys)}, not '{Short(v)}'.");
}

List<ExtraPayment> Extras(Dictionary<string, string?> o)
{
    if (!o.TryGetValue("extra", out var v) || v is null) return [];
    var list = new List<ExtraPayment>();
    foreach (var part in v.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var bits = part.Split(':');
        if (bits.Length != 2 || !int.TryParse(bits[0], NumberStyles.None, inv, out var n) || !decimal.TryParse(bits[1], NumberStyles.AllowDecimalPoint, inv, out var amt))
            throw new UsageException($"'--extra' takes payment:amount, like 6:500, not '{Short(part)}'.");
        list.Add(new ExtraPayment(n, amt));
        if (list.Count > LoanTerms.MaxPeriods) throw new UsageException("Too many extra payments.");
    }
    return list;
}

// echo user input back only shortened and without control characters
static string Short(string s) => new string(s.Take(40).Select(ch => char.IsControl(ch) ? '?' : ch).ToArray()) + (s.Length > 40 ? "…" : "");

static int Help()
{
    Console.WriteLine("""
        amortizer --amount 10000 --rate 12 --periods 12 [options]

          --amount N           amount borrowed
          --rate N             yearly rate in percent (a flat 1.5% a month is 18)
          --periods N          number of payments
          --frequency F        weekly, biweekly, monthly (default), quarterly, semiannual, annual
          --method M           annuity (default), equal-principal, interest-only, flat
          --day-count D        periodic (default), 30/360, 30e/360, act/360, act/365, act/act
          --decimals N         0 to 4 (default 2; use 0 for riel)
          --start DATE         when the money is paid out (default 2026-01-01)
          --first-payment DATE first payment date (default one period after the start)
          --grace N            interest-only payments at the start
          --fees N             fees taken at the start (counted in the APR)
          --extra P:N          extra principal N with payment P; repeat for more
          --schedule           print every payment
          --format F           text (default), csv or json

        Exit codes: 0 done, 2 invalid input.
        """);
    return 0;
}

sealed class UsageException(string message) : Exception(message);
