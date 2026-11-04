# Security

Amortizer takes numbers from people and from links, so the library, the `amortizer` command and the website all treat their input as untrusted.

## Reporting a problem

Please report vulnerabilities privately through GitHub's **Security → Report a vulnerability** on this repository, not in a public issue.

## What is protected, and how

| Threat | Protection | Tested by |
|---|---|---|
| **Runaway calculations** | Every input has a limit: 1,000,000,000,000,000 borrowed, 1000% a year, 6,000 payments, 4 decimals, a start date before the year 9000 and a first payment at most two years after it. The payment formula uses a discount factor that can't overflow, and the internal rate is found by bisection with a fixed number of steps. | `TheLargestLoansAllowedFinishQuicklyWithoutOverflow` (each method, with a time limit) |
| **Wrong figures** | Decimal arithmetic throughout, no floating point. Every schedule adds up to the cent and the last payment clears the balance. Principal is never negative and never more than what is owed. | `EveryRandomScheduleAddsUpToTheCent` (3,000 random loans), `ScheduleMatchesAnIndependentSimulation` |
| **Crashes on bad input** | `LoanTerms.Problems()` checks everything before any calculation and says what is wrong; `Loan.Build` refuses bad terms with `LoanTermsException`, and the limits keep every calculation inside `decimal`'s range (an effective rate too large to show is `null`, not an error). | `BadTermsAreRefusedWithAReason`, `NullTermsAreRefused` |
| **Hostile links to the website** | A link's query is read strictly. If a key is given twice, an escape is broken or there are more than 30 pairs, the whole link is ignored. A value longer than 40 characters is ignored. Numbers are plain decimals (no exponents, no signs). Names must be real values: `Enum.TryParse` also accepts `7` and `Annuity,FlatRate`, so `Enum.IsDefined` is checked too. Anything odd falls back to the defaults, and the result is checked by `Problems()` like any input. | `site/test/e2e.cjs`: 19 hostile links (overflow, duplicates, enum tricks, a 5,000-character value, markup, `__proto__`…) with no errors, dialogs or policy violations |
| **Script injection** | Blazor writes every value as text, never as markup, and the page has no `MarkupString`. The policy allows scripts only from the page's own origin (`script-src 'self' 'wasm-unsafe-eval'`), with no inline scripts. | e2e "markup in values stays out" |
| **Sending data anywhere** | No server, no analytics, no third-party files. `connect-src 'self'` only lets the page fetch its own files. | e2e: every check runs under the production headers |
| **Clickjacking** | `frame-ancestors 'none'` and `X-Frame-Options: DENY`. | e2e "the page can't be framed" (it fails with the headers removed) |
| **Terminal escapes from the command** | An invalid value is echoed in an error only after control characters are replaced and it's cut to 40 characters. | manual checks with escape sequences, `1e9`, duplicates and huge numbers (exit code 2 each time) |
| **Supply chain** | No runtime dependencies in the library or the command. The site uses only Microsoft's Blazor packages. The browser tests use one npm package (Playwright) at an exact version with a lockfile. CI runs `npm audit` and `dotnet list package --vulnerable`, and pins GitHub Actions to commits. | CI |

## Decisions worth knowing

- **No Trusted Types.** Iso20022Kit's viewer enforces Trusted Types; this site can't. Blazor's renderer inserts its own compiled static markup with `innerHTML`, so the browser blocks the app with `require-trusted-types-for 'script'`. A default policy that passes everything through would only look like protection. Values from the user and the link always go in as text nodes.
- **`style-src-attr 'unsafe-inline'`.** The long-schedule table only draws visible rows, and Blazor's `Virtualize` component sizes its spacers with inline `style` attributes. Style attributes can't run script; `<style>` elements and style sheets from elsewhere are still blocked (`style-src 'self'`).
- **Caching.** Files in `_framework/` with a fingerprint in their name are cached for a year. `dotnet.js` and `blazor.webassembly.js` have no fingerprint, and `dotnet.js` lists the names of all the others, so both are served with `no-cache`. Otherwise a returning visitor would keep a stale `dotnet.js` after a deploy and ask for files that no longer exist. This was found in the audit; the e2e test checks both headers.
- **The build downloads .NET.** Vercel has no .NET image, so `vercel.json` installs the SDK with Microsoft's `dotnet-install.sh` from `dot.net` over HTTPS. CI uses `actions/setup-dotnet` instead.

## What it doesn't do

- It doesn't check that a lender's offer is honest or legal. The figures are calculations.
- The website stores nothing. A loan is only kept in a link if you copy one, and that link contains the loan's numbers.
