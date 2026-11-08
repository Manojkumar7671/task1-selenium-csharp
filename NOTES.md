# Approach and limitations

## Approach
No hard-coded id / name / class / XPath. For each target field the page is scanned in the browser
(`locator.js`) and every candidate is scored by meaning:

| Signal | Weight |
|---|---|
| Linked label text, aria-label, aria-labelledby, placeholder, title | highest |
| Text just before the control (works for tables and plain divs) | high |
| name / id / autocomplete words (camelCase and _ split) | low |
| Look-alike words ("last", "middle", "country", ...) | heavy penalty |

- **Radio:** options are grouped; the group must read as "Gender/Sex" (legend, nearby text, container text),
  then the option whose text equals the wanted value wins. Word matching means "Male" never matches "Female".
- **Dropdown:** the select must read as "State/Province/Region"; +score if its options contain the wanted value.
- **Resilience extras:** polls up to 15 s for late-rendered fields, searches iframes, clicks the label or uses JS
  when a radio is custom-styled, verifies every value after setting it, and fails loudly (printing the best guess)
  instead of filling the wrong field.
- The only selector used is a marker attribute my own script puts on the winner, so it never depends on the site's markup.

## Limitations (honest)
- Native `<select>` only. Custom div-based dropdowns (React-Select etc.) are not handled.
- Cross-origin iframes and shadow DOM are not searched.
- Labels in a language other than English need extra keywords in `Program.cs`.
- If a page has two valid "First Name" fields (e.g. billing and shipping) the higher scorer wins; it can't know which one you meant.
- Scoring weights are hand-tuned and tested on 4 local variants plus the described changes, not on a large corpus.
- The locating script was tested in Chrome through Playwright; the C# wrapper was written for Selenium 4 but I could not compile it where I built it (no .NET SDK), so run `dotnet build` first and report any compile error.
