"""Build the bundle's local help page, dist/HydroComplete.bundle/Help/index.html.

PackageContents.xml points HelpFile at it, so F1 on a HydroComplete ribbon button
opens this page instead of the default AutoCAD help (Autodesk review 2026-11-14).
Content = docs/USER-GUIDE.md (via pandoc) + a command reference generated from
scripts/ribbon.json, one anchor per command (#hc_pipes etc.). Icons are copied in."""
import html
import json
import os
import shutil
import subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "dist", "HydroComplete.bundle", "Help")
spec = json.load(open(os.path.join(HERE, "ribbon.json"), encoding="utf-8"))

if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(os.path.join(OUT, "icons"))

body = subprocess.run(["pandoc", os.path.join(ROOT, "docs", "USER-GUIDE.md"), "-f", "gfm", "-t", "html"],
                      check=True, capture_output=True, text=True, encoding="utf-8").stdout

ref = ['<h2 id="command-reference">Command reference</h2>']
for p in spec:
    ref.append(f"<h3>{html.escape(p['name'])} panel</h3><table><tbody>")
    for b in p["buttons"]:
        stem = "hc_" + b["cmd"][3:].lower()
        shutil.copy(os.path.join(ROOT, "packaging", "icons", stem + "_32.png"), os.path.join(OUT, "icons"))
        ref.append(f'<tr id="{b["cmd"].lower()}"><td><img src="icons/{stem}_32.png" width="32" height="32" alt=""></td>'
                   f'<td><b>{html.escape(b["label"])}</b><br><code>{b["cmd"]}</code></td>'
                   f'<td>{html.escape(b["tip"])}</td></tr>')
    ref.append("</tbody></table>")

page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>HydroComplete for Civil 3D - Help</title>
<style>
body{{font-family:"Segoe UI",Arial,sans-serif;max-width:900px;margin:24px auto;padding:0 16px;color:#1d2b36;line-height:1.5}}
h1,h2,h3{{color:#1f6fb2}} code{{background:#eef2f6;padding:1px 4px;border-radius:3px}}
table{{border-collapse:collapse;width:100%;margin:8px 0 18px}} td,th{{border:1px solid #d5dde5;padding:6px 8px;vertical-align:top}}
blockquote{{border-left:4px solid #1f6fb2;margin:0;padding:4px 12px;background:#f4f8fc}}
</style></head><body>
{body}
{''.join(ref)}
<p>Support: <a href="mailto:support@hydrocomplete.com">support@hydrocomplete.com</a> -
<a href="https://hydrocomplete.com/civil3d">hydrocomplete.com/civil3d</a></p>
</body></html>
"""
open(os.path.join(OUT, "index.html"), "w", encoding="utf-8").write(page)
print("wrote", os.path.join(OUT, "index.html"))
