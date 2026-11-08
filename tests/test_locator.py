"""Runs locator.js against local page variants (no C# needed). Usage: python3 test_locator.py"""
import json, pathlib, sys, time
from playwright.sync_api import sync_playwright
here = pathlib.Path(__file__).parent
src = (here.parent / "locator.js").read_text()
FN = {"include": ["first name","firstname","fname","given name","forename"], "exclude": ["last","surname","middle","family","user","full"], "target": ""}
G = {"include": ["gender","sex"], "exclude": [], "target": "Male"}
S = {"include": ["state","province","region"], "exclude": ["country","city","zip","postal","address","statement"], "target": ""}
fails = 0
with sync_playwright() as p:
    b = p.chromium.launch(executable_path="/usr/bin/google-chrome", args=["--no-sandbox"])
    for f in sorted(here.glob("v*.html")):
        pg = b.new_page(); pg.goto(f.as_uri()); 
        if "late" in f.name: time.sleep(2)
        row = []
        for kind, cfg in (("text", FN), ("radio", G), ("select", S)):
            info = json.loads(pg.evaluate("([s,a])=>new Function(s).apply(null,a)", [src, [kind, cfg]]))
            desc = pg.evaluate("(k)=>{const e=document.querySelector('[data-ax-pick='+k+']');return e?e.outerHTML.slice(0,90)+' | ctx: '+(e.closest('div,tr,fieldset,label,section')||e.parentElement).innerText.replace(/\\s+/g,' ').slice(0,50):null}", kind) if info["found"] else None
            row.append((kind, info["found"], info["score"], desc))
        print("\n==", f.name)
        for r in row: print("  ", r)
        # expectations (ground truth by simple checks)
        exp = {"v1_original_like": ("fname","male","Select State"), "v2_reordered_renamed_decoys": ("zz_top","1","x91"),
               "v3_table_no_labels_placeholder": (None,"M",None), "v4_nested_late_render": (None,"r2",None)}.get(f.stem)
        pg.close()
    b.close()
