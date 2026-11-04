// The published site in Chromium, served with the production headers from vercel.json.
// Usage: node site/test/e2e.cjs <published wwwroot folder>
const { chromium, devices } = require("playwright");
const { spawn } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2] || "out/wwwroot");
const repo = path.resolve(__dirname, "../..");
const port = 4300 + Math.floor(Math.random() * 500);
const base = `http://127.0.0.1:${port}/`;
let failures = 0;
const check = (ok, name, detail = "") => {
  console.log(`${ok ? "ok  " : "FAIL"} ${name}${detail ? "  " + detail : ""}`);
  if (!ok) failures++;
};
const text = async (p, sel) => ((await p.textContent(sel)) || "").replace(/\s+/g, " ").trim();

// Links that try to break or mislead the page. Each must render either a schedule or a list of problems,
// with no script errors, no dialogs and no policy violations.
const hostileLinks = [
  ["decimal overflow is ignored", "?a=99999999999999999999999999999999&r=1", "figures"],
  ["exponent notation is ignored", "?a=1e9&r=5", "figures"],
  ["negative amount is ignored", "?a=-5", "figures"],
  ["a key given twice voids the link", "?a=1000&a=2000", "figures"],
  ["enum numbers are ignored", "?m=7&f=99&d=-1", "figures"],
  ["enum combinations are ignored", "?m=Annuity,FlatRate", "figures"],
  ["a huge term is refused", "?t=2147483647&tu=years&f=Weekly", "problems"],
  ["a huge rate is refused", "?r=1000&rp=month&t=600", "problems"],
  ["a monthly rate that overflows when made yearly", "?r=79228162514264337593543950335&rp=month", "problems"],
  ["grace longer than the loan", "?g=100000", "problems"],
  ["fees above the amount", "?fee=1000000", "problems"],
  ["a start date far in the future", "?s=9999-12-31", "problems"],
  ["a broken escape voids the link", "?a=%ZZ", "figures"],
  ["too many pairs void the link", "?" + "x=1&".repeat(40), "figures"],
  ["a 5,000-character value is ignored", "?a=" + "1".repeat(5000), "figures"],
  ["markup in values stays out", "?c=%3Cscript%3Ealert(1)%3C/script%3E&a=%3Cimg%20src=x%20onerror=alert(1)%3E", "figures"],
  ["prototype names are ordinary keys", "?__proto__=1&constructor=2", "figures"],
  ["zero interest", "?a=1000&r=0&m=Annuity", "figures"],
  ["the largest loan", "?a=1000000000000000&r=999&rp=year&t=115&tu=years&f=Weekly&m=Annuity", "figures"],
];

(async () => {
  const server = spawn("python3", [path.join(__dirname, "serve.py"), process.env.VERCEL_JSON || path.join(repo, "vercel.json"), root, String(port)], { stdio: "inherit" });
  try {
    for (let i = 0; i < 50; i++) {
      try { if ((await fetch(base)).ok) break; } catch { /* not up yet */ }
      await new Promise(r => setTimeout(r, 100));
    }

    // headers
    const page = await fetch(base);
    const csp = page.headers.get("content-security-policy") || "";
    const scriptSrc = (csp.match(/script-src [^;]*/) || [""])[0];
    check(scriptSrc === "script-src 'self' 'wasm-unsafe-eval'" && csp.includes("frame-ancestors 'none'") && csp.includes("object-src 'none'"), "strict Content-Security-Policy", scriptSrc);
    check(page.headers.get("x-frame-options") === "DENY", "X-Frame-Options: DENY");
    for (const f of ["dotnet.js", "blazor.webassembly.js"])
      check((await fetch(base + "_framework/" + f)).headers.get("cache-control") === "no-cache", `${f} isn't cached as immutable (its name has no fingerprint)`);
    const wasm = fs.readdirSync(path.join(root, "_framework")).find(n => /^System\.Private\.CoreLib\.[a-z0-9]+\.wasm$/.test(n));
    check(/immutable/.test((await fetch(base + "_framework/" + wasm)).headers.get("cache-control") || ""), "fingerprinted files are cached for good");

    const browser = await chromium.launch();
    const open = async (url, options = {}) => {
      const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, ...options });
      const p = await ctx.newPage();
      const problems = [];
      p.on("pageerror", e => problems.push("error: " + e.message));
      p.on("dialog", d => { problems.push("dialog: " + d.message()); d.dismiss(); });
      p.on("console", m => { if (m.type() === "error") problems.push("console: " + m.text().slice(0, 160)); });
      await p.addInitScript(() => {
        window.__csp = [];
        document.addEventListener("securitypolicyviolation", e => window.__csp.push(e.violatedDirective + " " + e.blockedURI));
      });
      await p.goto(url);
      await p.waitForSelector(".figures, .problems", { timeout: 30000 });
      const done = async () => [...problems, ...(await p.evaluate(() => window.__csp)).map(v => "csp: " + v)];
      return { ctx, p, done };
    };

    // the default loan: 1.5% a month flat for 12 months
    {
      const { ctx, p, done } = await open(base);
      const figures = await text(p, ".figures");
      check(figures.includes("98.33 USD") && figures.includes("APR 31.72%") && figures.includes("36.76%"), "flat 1.5%/month: payment 98.33, APR 31.72%, effective 36.76%", figures);
      check((await text(p, ".callout")).includes("100.14 USD"), "the flat-rate callout compares with a declining balance");
      const issues = await done();
      check(issues.length === 0, "no errors or policy violations", issues.join(" | "));
      await ctx.close();
    }

    for (const [name, query, expect] of hostileLinks) {
      const { ctx, p, done } = await open(base + query);
      const shown = (await p.$(".figures")) ? "figures" : "problems";
      const issues = await done();
      check(shown === expect && issues.length === 0, "link: " + name, shown !== expect ? `shows ${shown}` : issues.join(" | "));
      await ctx.close();
    }

    // a phone, the extra payments and the download
    {
      const { ctx, p, done } = await open(base, { ...devices["iPhone 13"], acceptDownloads: true });
      const [scroll, width] = await p.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
      check(scroll <= width, "no sideways scrolling on a phone", `page ${scroll}px, screen ${width}px`);
      await p.click("details.more summary");
      check((await text(p, ".extras")).includes("only reduce the interest"), "a flat-rate loan explains why it takes no extra payments");
      await p.locator("label", { hasText: "How interest is charged" }).locator("select").selectOption("Annuity");
      await p.click("text=+ Add an extra payment");
      await p.waitForSelector(".note");
      check((await text(p, ".figures")).includes("11 payments"), "an extra payment shortens the loan");
      const [download] = await Promise.all([p.waitForEvent("download"), p.click("text=Download CSV")]);
      const csv = fs.readFileSync(await download.path(), "utf8").trim().split("\n");
      check(download.suggestedFilename() === "loan-schedule.csv" && csv[0].startsWith("number,date,days,") && csv.length === 12, "the CSV holds the schedule", csv[0]);
      for (let i = 0; i < 30; i++) await p.click("text=+ Add an extra payment", { timeout: 500 }).catch(() => {});
      check((await p.locator(".extra").count()) === 24, "at most 24 extra payments");
      const issues = await done();
      check(issues.length === 0, "phone: no errors or policy violations", issues.join(" | "));
      await ctx.close();
    }

    // 5,980 weekly payments: the table only draws what is visible
    {
      const { ctx, p, done } = await open(base + "?a=100000&r=5&rp=year&t=115&tu=years&f=Weekly&m=Annuity");
      const rows = await p.locator(".tbody .tr").count();
      check((await text(p, ".figures")).includes("5980 payments") && rows < 100, "a 5,980-payment schedule draws only visible rows", `${rows} rows`);
      check((await done()).length === 0, "large schedule: no errors");
      await ctx.close();
    }

    // the page can't be framed by another origin
    {
      const ctx = await browser.newContext();
      const p = await ctx.newPage();
      await p.setContent(`<iframe src="${base}" width="400" height="300"></iframe>`);
      await p.waitForTimeout(1500);
      const frame = p.frames().find(f => f !== p.mainFrame());
      const framed = frame ? await frame.evaluate(() => !!document.querySelector(".brand")).catch(() => false) : false;
      check(!framed, "the page can't be framed");
      await ctx.close();
    }

    await browser.close();
  } finally {
    server.kill();
  }
  console.log(failures ? `\n${failures} failed` : "\nall passed");
  process.exit(failures ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
