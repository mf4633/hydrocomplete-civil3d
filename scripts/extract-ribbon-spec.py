"""One-off: pull the panel/button table out of the old programmatic RibbonBuilder.cs
into scripts/ribbon.json, which is now the single source for the CUIX ribbon, the
button icons and PackageContents.xml."""
import json
import re
import sys

src = open(sys.argv[1], encoding="utf-8-sig").read()
panels = []
for m in re.finditer(r'AddPanel\("([^"]+)",(.*?)\);', src, re.S):
    btns = re.findall(r'\("([^"]*)", "(HC_\w+)", "([^"]*)"\)', m.group(2))
    panels.append({
        "name": m.group(1),
        "buttons": [
            {"cmd": c, "label": t.replace("\\n", " ").replace("- ", "").strip(), "tip": tip}
            for (t, c, tip) in btns
        ],
    })
json.dump(panels, open(sys.argv[2], "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print(sum(len(p["buttons"]) for p in panels), [(p["name"], len(p["buttons"])) for p in panels])
