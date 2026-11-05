# Amortizer

**Website: [amortizer-steel.vercel.app](https://amortizer-steel.vercel.app)** · runs in your browser; nothing is uploaded.

What does this loan really cost? Amortizer builds a loan's full payment schedule with exact decimal arithmetic and works out its true yearly cost: the APR and the effective annual rate.

It comes as a .NET library, an `amortizer` command and a website (`site/`) that runs entirely in the browser.

## Install

On NuGet: [Amortizer](https://www.nuget.org/packages/Amortizer) · [Amortizer.Cli](https://www.nuget.org/packages/Amortizer.Cli). For .NET 8 and .NET 10.

```sh
dotnet add package Amortizer
dotnet tool install --global Amortizer.Cli   # the `amortizer` command
```

## Why

Many small loans, from microfinance to consumer and motorbike loans, are quoted at a *flat* rate: interest is charged on the full amount for the whole term, even as it's paid back. "1.5% a month" sounds like 18% a year. In fact it costs as much as **31.7% a year** on a declining balance, or **36.8%** compounded:

```
$ amortizer --amount 1000 --rate 18 --periods 12 --method flat
Payment        98.33
Total interest 180.00
Total paid     1,180.00
APR            31.715%
Effective rate 36.757% a year
Paid off       2027-01-01 (12 payments)
```

Amortizer shows that number for any loan, with every payment listed and every schedule adding up to the cent.

## What it handles

- **Methods**: equal payments (annuity), equal principal, interest only with the principal at the end, and flat rate.
- **Frequencies**: weekly, every two weeks, monthly, quarterly, twice a year and yearly.
- **Day counts**:
  - periodic (each payment is exactly 1/12 of a year, and so on);
  - 30/360 (bond basis);
  - 30E/360;
  - ACT/360;
  - ACT/365 fixed;
  - ACT/ACT ISDA.

  With a day count, a first period that is longer or shorter than the others is charged for the days it really has.
- **Grace periods** (interest only at the start), **extra payments** that reduce the principal, and **upfront fees** counted in the APR.
- **Currencies with 0 to 4 decimals**, for example 0 for riel, with a choice of rounding.
- **The true cost**:
  - APR: the internal rate of return of what is received and what is paid, times the payments per year.
  - Effective annual rate: the APR compounded over a year.

## .NET

```csharp
using Amortizer;

var schedule = Loan.Build(new LoanTerms
{
    Principal = 200_000m,
    AnnualRatePercent = 6.5m,
    Periods = 360,
});

Console.WriteLine(schedule.RegularPayment);      // 1264.14
Console.WriteLine(schedule.TotalInterest);       // 255085.82
foreach (var row in schedule.Rows)
    Console.WriteLine($"{row.Number} {row.Date} {row.Interest} {row.Principal} {row.ClosingBalance}");

File.WriteAllText("schedule.csv", schedule.ToCsv());
```

Every amount is a `decimal`. Each row's interest and principal add up to its payment, the principal paid adds up to the amount borrowed, and the last payment clears the balance exactly. Those invariants are checked on 3,000 random loans in the tests.

`LoanTerms.Problems()` lists what's wrong with a set of terms in plain words ("Fees must be less than the amount borrowed."), and `Loan.Build` throws `LoanTermsException` with that list. The inputs have limits, so no loan can make the calculation run away:
- at most 1,000,000,000,000,000 borrowed;
- at most 1000% a year;
- at most 6,000 payments.

## Command line

```
dotnet run --project src/Amortizer.Cli -- --amount 10000 --rate 12 --periods 12 --schedule
dotnet run --project src/Amortizer.Cli -- --amount 10000 --rate 12 --periods 12 --format csv > schedule.csv
dotnet run --project src/Amortizer.Cli -- --help
```

`--format json` prints the summary and every row. The command exits with 0 when it worked and 2 for invalid input; it's packaged as a .NET tool named `amortizer`.

## Website

`site/Amortizer.Web` is a Blazor WebAssembly app that uses the same library, so the site and the library always agree. It does the following:
- shows the figures as you type, compares a flat rate with the same rate on a declining balance, and draws the balance over time;
- lists every payment; long schedules only draw the visible rows;
- downloads the schedule as CSV;
- makes a link that reproduces the loan.

Nothing is sent anywhere: there's no server and no analytics, and the page's Content-Security-Policy only lets it load its own files.

```
dotnet publish site/Amortizer.Web -c Release -o out     # static site in out/wwwroot
cd site/test && npm ci && npx playwright install chromium
node e2e.cjs ../../out/wwwroot                            # the site in Chromium, under the production headers
```

## Tests

```
dotnet test
```

They cover:
- known schedules (a 30-year mortgage, a flat microfinance loan, equal principal, interest only, grace periods, extra payments, fees);
- every day-count convention, including month ends and leap years;
- the invariants on random loans;
- limits and bad input;
- the largest loans finishing quickly.

The browser tests in `site/test` load the published site under the headers in `vercel.json`, open 19 hostile links, and check:
- the figures, the extra payments, the CSV download and the phone layout;
- that a 5,980-payment schedule renders;
- that the page can't be framed.

## Security

See [SECURITY.md](SECURITY.md).

## What it isn't

- Not an offer of credit or financial advice. The figures are calculations; a lender's own schedule is what counts.
- Not a legal APR for any particular country. Regulations such as the US Truth in Lending Act (Regulation Z) or the EU Consumer Credit Directive define exactly which fees count and how the rate is rounded. Amortizer's APR is the plain actuarial one: the periodic internal rate times the number of payments a year.

## Licence

Apache-2.0 (see [LICENSE](LICENSE)). Written with the help of Claude (Anthropic).
