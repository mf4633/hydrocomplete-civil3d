"""Writes scripts/ribbon.json: the single source for the CUIX ribbon (build-cuix.ps1),
the button icons (build-icons.py) and the Commands lists in PackageContents.xml
(gen-packagecontents.py).

Layout rule learned on the Field Kit review: Civil 3D cannot draw a ribbon row with
more than ~3 small buttons, so every panel holds at most 6 commands (3 rows x 2) and
the 53 commands are spread over 13 panels."""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
old = {b["cmd"]: b for p in json.load(open(os.path.join(HERE, "ribbon-legacy.json"), encoding="utf-8")) for b in p["buttons"]}
old["HC_STM_IMPORT"] = {"cmd": "HC_STM_IMPORT", "label": "STM Import",
                        "tip": "Import a Hydraflow Storm Sewers .stm project into the drawing."}

# panel name, colour, [(cmd, glyph)]
LAYOUT = [
    ("Pipes", "#1f6fb2", [("HC_PIPES", "pipe"), ("HC_PIPES_WRITE", "pipe_label"), ("HC_CAPACITY", "gauge"),
                         ("HC_CAPACITY_WRITE", "gauge_label"), ("HC_SIZE", "ruler"), ("HC_MULTIRP", "bars")]),
    ("Network", "#1f6fb2", [("HC_NETWORK", "network"), ("HC_VALIDATE", "check"), ("HC_NETWORK_EDIT", "pencil"),
                           ("HC_NETWORK_DIAGRAM", "schematic")]),
    ("Rainfall", "#2a8a9e", [("HC_RATIONAL", "rain"), ("HC_ATLAS14", "idf"), ("HC_TC", "clock"),
                            ("HC_SOIL", "soil"), ("HC_LOSS", "infiltrate")]),
    ("Runoff", "#2a8a9e", [("HC_SCS", "drop"), ("HC_UNIT_HYDRO", "unit_hydro"), ("HC_HYDROGRAPH", "hydrograph"),
                          ("HC_ROUTE_HYDRO", "route"), ("HC_CONTINUOUS", "calendar")]),
    ("Detention", "#3c7d3c", [("HC_DETENTION", "pond"), ("HC_PREPOST", "prepost"), ("HC_BIORETENTION", "plant"),
                             ("HC_WETLAND", "reeds")]),
    ("Water Quality", "#3c7d3c", [("HC_WQV", "drop_check"), ("HC_BMP_SIZE", "box_size"), ("HC_WQ_TRAIN", "train"),
                                 ("HC_WQ_DIAGRAM", "schematic"), ("HC_OPTIMIZE", "star")]),
    ("Sediment", "#9a6b2f", [("HC_SEDIMENT", "sediment"), ("HC_SEDIMENT_BASIN", "basin")]),
    ("Hydraulics", "#5b4aa8", [("HC_HGL", "hgl"), ("HC_GVF", "wave"), ("HC_PROFILE", "profile"),
                              ("HC_PROFILE_DXF", "export")]),
    ("Structures", "#5b4aa8", [("HC_CULVERT", "culvert"), ("HC_INLETS", "grate"), ("HC_PUMP", "pump")]),
    ("Analysis", "#b2452f", [("HC_ANALYZE", "magnifier"), ("HC_REVIEW", "clipboard"), ("HC_REPORT", "document"),
                            ("HC_REPORT_PDF", "document_pdf")]),
    ("Model Builder", "#b2452f", [("HC_DAG", "nodes"), ("HC_DAG_SAVE", "save"), ("HC_DAG_LOAD", "folder")]),
    ("Data", "#546270", [("HC_LANDXML", "export"), ("HC_LANDXML_IMPORT", "import"), ("HC_STM_IMPORT", "import"),
                        ("HC_BACKGROUND", "map"), ("HC_COST", "dollar")]),
    ("HydroComplete", "#546270", [("HC_ACTIVATE", "key"), ("HC_LICENSE", "badge"), ("HC_ABOUT", "info")]),
]

out = []
for name, colour, cmds in LAYOUT:
    out.append({"name": name, "colour": colour, "buttons": [
        {"cmd": c, "label": old[c]["label"], "tip": old[c]["tip"], "glyph": g} for c, g in cmds]})

all_cmds = [b["cmd"] for p in out for b in p["buttons"]]
assert len(all_cmds) == len(set(all_cmds)) == 53, len(all_cmds)
assert set(all_cmds) == set(old), set(old) ^ set(all_cmds)
assert max(len(p["buttons"]) for p in out) <= 6
json.dump(out, open(os.path.join(HERE, "ribbon.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print("ribbon.json:", len(out), "panels,", len(all_cmds), "commands")
