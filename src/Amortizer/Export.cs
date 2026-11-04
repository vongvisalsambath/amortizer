using System.Globalization;
using System.Text;

namespace Amortizer;

/// <summary>Schedules as CSV.</summary>
public static class Export
{
    /// <summary>
    /// The schedule as CSV with a header row. Every cell is a number or an ISO date written with the invariant culture,
    /// so the file reads the same everywhere and can't carry spreadsheet formulas.
    /// </summary>
    public static string ToCsv(this Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("number,date,days,opening_balance,interest,principal,extra,payment,closing_balance\n");
        foreach (var r in schedule.Rows)
            sb.Append(c, $"{r.Number},{r.Date:yyyy-MM-dd},{r.Days},{r.OpeningBalance},{r.Interest},{r.Principal},{r.Extra},{r.Payment},{r.ClosingBalance}\n");
        return sb.ToString();
    }
}
