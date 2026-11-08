# Resilient Form Automation (C# + Selenium)

Fills **First Name** (text), **Gender** (radio) and **State** (dropdown) on the CloudQA practice form,
and keeps working when the page changes. The page URL is configurable.

## Setup
- .NET 8 SDK, Google Chrome (Selenium Manager downloads the matching driver automatically)

## Run
```bash
dotnet run -- --url https://app.cloudqa.io/home/AutomationPracticeForm
# options: --first-name Manoj --gender Female --state Kerala --headless --keep-open
# URL can also come from env var:  FORM_URL=<url> dotnet run
```
Exit code is `0` if all three checks pass, `1` otherwise. Output shows PASS/FAIL per field and which element matched (and its score).

## Test without the real site
`tests/` has four local page variants (reordered fields, renamed ids, decoy fields, table layout,
placeholder-only labels, late-rendered content):
```bash
dotnet run -- --url file:///$(pwd)/tests/v2_reordered_renamed_decoys.html --headless
python3 tests/test_locator.py     # runs just the locating logic (needs: pip install playwright)
```

## How it works
See `NOTES.md`. Short version: `locator.js` scores every candidate control by what a human would read
(label, aria-label, placeholder, nearby text, name/id words, dropdown options), subtracts points for
look-alikes ("Last Name", "Country"), and picks the best match above a threshold.
