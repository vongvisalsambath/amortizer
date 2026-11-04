using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Amortizer.Web.Pages;

public partial class Home
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Dictionary<string, int> Currencies = new() { ["USD"] = 2, ["KHR"] = 0, ["THB"] = 2, ["EUR"] = 2 };
    private const int MaxExtras = 24;

    private sealed class Extra { public int Period { get; set; } = 1; public decimal Amount { get; set; } }

    // the form (defaults: a typical "1.5% a month flat" microfinance loan, to show the point straight away)
    private decimal amount = 1000m;
    private string currency = "USD";
    private decimal rate = 1.5m;
    private string ratePer = "month";
    private int term = 12;
    private string termUnit = "months";
    private Method method = Method.FlatRate;
    private Frequency frequency = Frequency.Monthly;
    private DayCount dayCount = DayCount.Periodic;
    private decimal fees;
    private int grace;
    private DateOnly start = new(2026, 1, 1);
    private readonly List<Extra> extras = [];

    private Schedule? schedule, compare;
    private List<Row> rowList = [];
    private List<string> problems = [];
    private List<(double X, double W, double Interest, double Principal)> bars = [];
    private string balanceLine = "";
    private string copyLabel = "Copy a link to this loan";

    private bool ExtrasApply => method is Method.Annuity or Method.EqualPrincipal;
    private decimal QuotedYearly => ratePer == "month" ? rate * 12 : rate;
    private string FrequencyText => frequency switch
    {
        Frequency.Weekly => "weekly", Frequency.Biweekly => "every two weeks", Frequency.Monthly => "monthly",
        Frequency.Quarterly => "quarterly", Frequency.SemiAnnual => "twice a year", _ => "yearly",
    };

    protected override void OnInitialized()
    {
        ReadLink();
        Recalc();
    }

    /// <summary>Reads a shared link. Every value is parsed strictly and checked; anything odd is ignored.</summary>
    private void ReadLink()
    {
        var uri = new Uri(Nav.Uri);
        if (uri.Query.Length is 0 or > 2000) return;
        var q = ParseQuery(uri.Query);
        string? Get(string k) => q.TryGetValue(k, out var v) && v.Length <= 40 ? v : null;
        if (decimal.TryParse(Get("a"), NumberStyles.AllowDecimalPoint, Inv, out var a)) amount = a;
        if (Get("c") is string c && Currencies.ContainsKey(c)) currency = c;
        if (decimal.TryParse(Get("r"), NumberStyles.AllowDecimalPoint, Inv, out var r)) rate = r;
        if (Get("rp") is "year" or "month") ratePer = Get("rp")!;
        if (int.TryParse(Get("t"), NumberStyles.None, Inv, out var t)) term = t;
        if (Get("tu") is "months" or "years") termUnit = Get("tu")!;
        if (Enum.TryParse<Method>(Get("m"), out var m) && Enum.IsDefined(m)) method = m;
        if (Enum.TryParse<Frequency>(Get("f"), out var f) && Enum.IsDefined(f)) frequency = f;
        if (Enum.TryParse<DayCount>(Get("d"), out var d) && Enum.IsDefined(d)) dayCount = d;
        if (decimal.TryParse(Get("fee"), NumberStyles.AllowDecimalPoint, Inv, out var fe)) fees = fe;
        if (int.TryParse(Get("g"), NumberStyles.None, Inv, out var g)) grace = g;
        if (DateOnly.TryParseExact(Get("s"), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var s)) start = s;
        // Enum.TryParse also accepts numbers ("7"); IsDefined above keeps only real values
    }

    private string Link()
    {
        var p = new Dictionary<string, string?>
        {
            ["a"] = amount.ToString(Inv), ["c"] = currency, ["r"] = rate.ToString(Inv), ["rp"] = ratePer, ["t"] = term.ToString(Inv), ["tu"] = termUnit,
            ["m"] = method.ToString(), ["f"] = frequency.ToString(), ["d"] = dayCount.ToString(), ["fee"] = fees.ToString(Inv), ["g"] = grace.ToString(Inv),
            ["s"] = start.ToString("yyyy-MM-dd", Inv),
        };
        return Nav.BaseUri + "?" + string.Join('&', p.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value ?? "")));
    }

    /// <summary>key=value pairs; a key given twice, a malformed escape or more than 30 pairs and the link is ignored.</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 30) return [];
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            try
            {
                var key = Uri.UnescapeDataString(part[..eq]);
                var value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
                if (!result.TryAdd(key, value)) return [];
            }
            catch (UriFormatException) { return []; }
        }
        return result;
    }

    private int Periods()
    {
        var ppy = Calendar.PerYear(frequency);
        var years = termUnit == "years" ? (decimal)term : term / 12m;
        var n = years * ppy;
        return n is > 0 and < int.MaxValue ? (int)Math.Max(1, Math.Round(n)) : 0;
    }

    private void Recalc()
    {
        schedule = compare = null;
        problems = [];
        try
        {
            var terms = new LoanTerms
            {
                Principal = amount,
                AnnualRatePercent = QuotedYearly,
                Periods = Periods(),
                Method = method,
                Frequency = frequency,
                DayCount = dayCount,
                Decimals = Currencies[currency],
                UpfrontFees = fees,
                GracePeriods = grace,
                StartDate = start,
                ExtraPayments = !ExtrasApply ? [] : extras.Where(x => x.Amount > 0).Select(x => new ExtraPayment(x.Period, x.Amount)).ToList(),
            };
            problems = terms.Problems().ToList();
            if (problems.Count > 0) return;
            schedule = Loan.Build(terms);
            rowList = schedule.Rows.ToList();
            if (method == Method.FlatRate) compare = Loan.Build(terms with { Method = Method.Annuity });
            Chart(schedule);
        }
        catch (OverflowException)
        {
            problems = ["Those numbers are too large to calculate."];
        }
    }

    private void Chart(Schedule s)
    {
        // up to 120 bars: payments are grouped when there are more
        var rows = s.Rows;
        var groups = Math.Min(120, rows.Count);
        var per = (double)rows.Count / groups;
        var sums = new List<(double I, double P)>();
        for (var g = 0; g < groups; g++)
        {
            var from = (int)Math.Floor(g * per);
            var to = Math.Max(from + 1, (int)Math.Floor((g + 1) * per));
            double i = 0, p = 0;
            for (var k = from; k < to && k < rows.Count; k++) { i += (double)rows[k].Interest; p += (double)rows[k].Principal; }
            sums.Add((i, p));
        }
        var maxBar = Math.Max(1e-9, sums.Max(x => x.I + x.P));
        var w = 600.0 / groups;
        bars = sums.Select((x, g) => (g * w + w * 0.12, w * 0.76, x.I / maxBar * 70, x.P / maxBar * 70)).ToList();
        var top = (double)s.Terms.Principal;
        var pts = new List<string> { $"0,{F(10)}" };
        for (var k = 0; k < rows.Count; k++)
            if (rows.Count <= 600 || k % (rows.Count / 600 + 1) == 0 || k == rows.Count - 1)
                pts.Add($"{F((k + 1) * 600.0 / rows.Count)},{F(10 + (1 - (double)rows[k].ClosingBalance / top) * 95)}");
        balanceLine = string.Join(' ', pts);
    }

    private void AddExtra()
    {
        if (extras.Count >= MaxExtras) return;
        extras.Add(new Extra { Period = Math.Max(1, Math.Min(12, Periods() / 2)), Amount = Math.Round(amount / 10, Currencies[currency]) });
        Recalc();
    }

    private async Task Download()
    {
        if (schedule is null) return;
        await JS.InvokeVoidAsync("amortizerDownload", "loan-schedule.csv", schedule.ToCsv());
    }

    private async Task CopyLink()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", Link());
            copyLabel = "Link copied";
        }
        catch (JSException) { copyLabel = "Couldn't copy: your browser blocked it"; }
    }

    private string Money(decimal v) => v.ToString("N" + Currencies[currency], Inv) + " " + currency;
    private string Num(decimal v) => v.ToString("N" + Currencies[currency], Inv);
    private static string Pct(decimal? v) => v is decimal d ? d.ToString(d >= 100 ? "N0" : "0.##", Inv) + "%" : "too high to show";
    private static string F(double v) => v.ToString("0.##", Inv);
}
