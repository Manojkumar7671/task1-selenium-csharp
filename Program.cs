using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

// Resilient form automation: First Name (text), Gender (radio), State (dropdown).
// Fields are found by meaning (see locator.js), not by fixed id/name/xpath/css.

var opt = Options.Parse(args);
var locatorJs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "locator.js"));

var co = new ChromeOptions();
if (opt.Headless) co.AddArgument("--headless=new");
co.AddArgument("--window-size=1366,900");
using var driver = new ChromeDriver(co);
driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

var results = new List<(string name, bool ok, string detail)>();
try
{
    Console.WriteLine($"Opening {opt.Url}");
    driver.Navigate().GoToUrl(opt.Url);
    new WebDriverWait(driver, TimeSpan.FromSeconds(20)).Until(d =>
        ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")!.Equals("complete"));

    results.Add(Step("First Name (text box)", () => FillFirstName()));
    results.Add(Step("Gender (radio)", () => PickGender()));
    results.Add(Step("State (dropdown)", () => PickState()));
}
finally
{
    if (opt.KeepOpen) { Console.WriteLine("Press Enter to close browser..."); Console.ReadLine(); }
    driver.Quit();
}

Console.WriteLine("\n=== RESULT ===");
foreach (var r in results) Console.WriteLine($"{(r.ok ? "PASS" : "FAIL")}  {r.name}  - {r.detail}");
return results.All(r => r.ok) ? 0 : 1;

// ---------------------------------------------------------------- steps

(string, bool, string) Step(string name, Func<string> action)
{
    try { return (name, true, action()); }
    catch (Exception e) { return (name, false, e.Message.Split('\n')[0]); }
}

string FillFirstName()
{
    var el = Locate("text", new { include = new[] { "first name", "firstname", "fname", "given name", "forename" },
        exclude = new[] { "last", "surname", "middle", "family", "user", "full" }, target = "" });
    ScrollTo(el);
    el.Clear();
    el.SendKeys(opt.FirstName);
    if (el.GetAttribute("value") != opt.FirstName)
    {   // fallback for inputs that swallow key events
        Js("arguments[0].value=arguments[1];arguments[0].dispatchEvent(new Event('input',{bubbles:true}));arguments[0].dispatchEvent(new Event('change',{bubbles:true}));", el, opt.FirstName);
    }
    var v = el.GetAttribute("value");
    if (v != opt.FirstName) throw new Exception($"value is '{v}', expected '{opt.FirstName}'");
    return $"typed '{v}'";
}

string PickGender()
{
    var el = Locate("radio", new { include = new[] { "gender", "sex" }, exclude = Array.Empty<string>(), target = opt.Gender });
    ScrollTo(el);
    if (!el.Selected)
    {
        try { el.Click(); } catch { }
        if (!el.Selected)   // custom-styled radios: click the label, then JS as last resort
        {
            try { Js("var l=arguments[0].labels&&arguments[0].labels[0]; if(l) l.click();", el); } catch { }
        }
        if (!el.Selected) Js("arguments[0].click();", el);
    }
    if (!el.Selected) throw new Exception($"'{opt.Gender}' could not be selected");
    return $"selected '{opt.Gender}'";
}

string PickState()
{
    var el = Locate("select", new { include = new[] { "state", "province", "region" },
        exclude = new[] { "country", "city", "zip", "postal", "address", "statement" }, target = opt.State ?? "" });
    ScrollTo(el);
    var sel = new SelectElement(el);
    IWebElement? chosen = null;
    if (!string.IsNullOrWhiteSpace(opt.State))
    {
        string want = Norm(opt.State);
        chosen = sel.Options.FirstOrDefault(o => Norm(o.Text) == want || Norm(o.GetAttribute("value") ?? "") == want)
              ?? sel.Options.FirstOrDefault(o => Norm(o.Text).Contains(want));
        if (chosen == null) throw new Exception($"option '{opt.State}' not found in dropdown");
    }
    else
    {   // no value requested: first real option (skip placeholders like "Select State")
        chosen = sel.Options.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Text) &&
                 !Norm(o.Text).StartsWith("select") && !Norm(o.Text).StartsWith("choose") && !Norm(o.Text).StartsWith("please"))
              ?? throw new Exception("dropdown has no selectable options");
    }
    sel.SelectByText(chosen.Text);
    if (sel.SelectedOption.Text != chosen.Text) throw new Exception("selection did not stick");
    return $"selected '{chosen.Text}'";
}

// ---------------------------------------------------------------- locating

IWebElement Locate(string kind, object cfg)
{
    var deadline = DateTime.UtcNow.AddSeconds(15);   // pages may render late
    string lastWhy = "";
    while (DateTime.UtcNow < deadline)
    {
        var contexts = FrameCount();                  // -1 = main document, 0..n-1 = iframes
        (int frame, double score, bool found, string why)? best = null;
        for (int f = -1; f < contexts; f++)
        {
            if (!SwitchTo(f)) continue;
            var json = (string)Js(locatorJs, kind, cfg)!;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool found = root.GetProperty("found").GetBoolean();
            double score = root.GetProperty("score").ValueKind == JsonValueKind.Null ? -999 : root.GetProperty("score").GetDouble();
            string why = root.GetProperty("why").GetString() ?? "";
            if (best == null || (found && !best.Value.found) || (found == best.Value.found && score > best.Value.score))
                best = (f, score, found, why);
        }
        if (best != null && best.Value.found)
        {
            SwitchTo(best.Value.frame);
            Console.WriteLine($"  [{kind}] matched (score {best.Value.score}): {best.Value.why}");
            return driver.FindElement(By.CssSelector($"[data-ax-pick='{kind}']"));
        }
        lastWhy = best?.why ?? "no candidates";
        Thread.Sleep(500);
    }
    throw new Exception($"no confident match for {kind}; best guess was: {lastWhy}");
}

int FrameCount() { driver.SwitchTo().DefaultContent(); return driver.FindElements(By.TagName("iframe")).Count; }

bool SwitchTo(int frame)
{
    driver.SwitchTo().DefaultContent();
    if (frame < 0) return true;
    try { driver.SwitchTo().Frame(driver.FindElements(By.TagName("iframe"))[frame]); return true; }
    catch { return false; }   // cross-origin / detached frame
}

object? Js(string script, params object?[] a) => ((IJavaScriptExecutor)driver).ExecuteScript(script, a);
void ScrollTo(IWebElement e) => Js("arguments[0].scrollIntoView({block:'center'});", e);
static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

// ---------------------------------------------------------------- options

record Options(string Url, string FirstName, string Gender, string? State, bool Headless, bool KeepOpen)
{
    public static Options Parse(string[] a)
    {
        string url = Environment.GetEnvironmentVariable("FORM_URL") ?? "https://app.cloudqa.io/home/AutomationPracticeForm";
        string first = "Manoj", gender = "Male"; string? state = null; bool headless = false, keep = false;
        for (int i = 0; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--url": url = a[++i]; break;
                case "--first-name": first = a[++i]; break;
                case "--gender": gender = a[++i]; break;
                case "--state": state = a[++i]; break;
                case "--headless": headless = true; break;
                case "--keep-open": keep = true; break;
                default: Console.WriteLine($"Unknown option {a[i]}"); Environment.Exit(2); break;
            }
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) { Console.WriteLine("Invalid --url"); Environment.Exit(2); }
        return new Options(url, first, gender, state, headless, keep);
    }
}
