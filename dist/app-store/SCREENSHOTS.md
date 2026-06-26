# Screenshot Capture Guide

Shot list for Michael to capture in **Civil 3D 2026** on a storm-sewer test drawing.
Target resolution: **1920×1080** (or Publisher portal minimum). Save as PNG.

**Recommended test drawing:** `C-STORM` (30 pipes, AutoCAD 2018-format DWG) — same drawing used for README validation.

**Publisher captions:** Ready-to-paste text in `SCREENSHOT_CAPTIONS.md` (10 shots for v1.4.0 portal upload).

---

## Before you shoot

1. Run `install.ps1` so the bundle auto-loads (banner shows **v1.4.0**).
2. Open the test DWG with pipe networks visible in plan.
3. Set a clean visual style: light background, pipe network color distinct, labels readable.
4. Hide unrelated palettes if they clutter the frame; keep the Civil 3D ribbon visible in at least one shot.
5. Scrub client names / addresses from title block if present.
6. For live Atlas 14 shots, set `GEOGRAPHICLOCATION` on the drawing (or use a geo-referenced test DWG).
7. For `HC_ANALYZE`, `HC_DETENTION`, and `HC_PREPOST`, use a drawing with catchments and/or a detention scenario (may require a second test DWG beyond `C-STORM`).

---

## Command reference (52 commands — v1.7.2)

| Category | Commands |
|---|---|
| **Network & capacity** | `HC_NETWORK`, `HC_PIPES`, `HC_PIPES_WRITE`, `HC_CAPACITY`, `HC_CAPACITY_WRITE`, `HC_SIZE`, `HC_VALIDATE`, `HC_MULTIRP`, `HC_NETWORK_EDIT`, `HC_NETWORK_DIAGRAM`, `HC_COST` |
| **Hydrology & analysis** | `HC_RATIONAL`, `HC_TC`, `HC_SCS`, `HC_UNIT_HYDRO`, `HC_HYDROGRAPH`, `HC_ROUTE_HYDRO`, `HC_ATLAS14`, `HC_LOSS`, `HC_CONTINUOUS`, `HC_ANALYZE`, `HC_PREPOST`, `HC_OPTIMIZE` |
| **Hydraulics & structures** | `HC_HGL`, `HC_INLETS`, `HC_CULVERT`, `HC_GVF`, `HC_PUMP`, `HC_PROFILE`, `HC_PROFILE_DXF` |
| **BMPs & detention** | `HC_WQV`, `HC_DETENTION`, `HC_BMP_SIZE`, `HC_WQ_TRAIN`, `HC_WQ_DIAGRAM`, `HC_SEDIMENT`, `HC_SEDIMENT_BASIN`, `HC_BIORETENTION`, `HC_WETLAND`, `HC_SOIL` |
| **Compliance & exchange** | `HC_REVIEW`, `HC_LANDXML`, `HC_LANDXML_IMPORT`, `HC_REPORT`, `HC_REPORT_PDF`, `HC_BACKGROUND` |
| **Visual model builder** | `HC_DAG`, `HC_DAG_SAVE`, `HC_DAG_LOAD` (net8, Civil 3D 2025/2026 only) |
| **License & help** | `HC_ACTIVATE`, `HC_LICENSE`, `HC_ABOUT` |

---

## Shot 1 — HydroComplete Ribbon Tab

**File name:** `01-ribbon-tab.png`  
**Portal caption:** See `SCREENSHOT_CAPTIONS.md` #1

| Field | Detail |
|---|---|
| **What to show** | Full Civil 3D window with **HydroComplete › Analysis** ribbon tab active |
| **Buttons visible** | Pipe Capacity, Write Capacity, Design Capacity, Write Overload, HGL Profile, HTML Report, PDF Report, Rational Q, Atlas 14 IDF, Activate Pro, License, About (per `RibbonBuilder.cs`) |
| **Tips** | Zoom drawing so a pipe network is visible behind the ribbon; confirms in-product integration |

---

## Shot 2 — HC_PIPES Command Output

**File name:** `02-hc-pipes-output.png`  
**Portal caption:** #2

| Field | Detail |
|---|---|
| **What to show** | Command line / text window after running `HC_PIPES` |
| **Data visible** | Pipe count (e.g. 30 pipes), diameter in feet (2.00 = 24″), Q<sub>full</sub> (cfs), V<sub>full</sub> (ft/s), surcharge flags |
| **Tips** | Expand command history tall enough to show 3–4 representative pipes + summary line; optionally split view with plan |

---

## Shot 3 — HC_CAPACITY Overload Check

**File name:** `03-hc-capacity-output.png`  
**Portal caption:** #3

| Field | Detail |
|---|---|
| **What to show** | Command line output after `HC_CAPACITY` with a design *Q* that surcharges at least one pipe |
| **Data visible** | Header row (`Q_full`, `Q_des`, `Q_des/Q`, `d/D`, `SURCH`); mix of OK and `*` surcharged pipes; summary count of overloaded pipes |
| **Tips** | Use a design *Q* high enough to flag 2–3 pipes; show the summary line *N pipe(s) surcharged* |

---

## Shot 4 — HC_HGL Normal-Depth Profile + Labels

**File name:** `04-hc-hgl-profile.png`  
**Portal caption:** #4

| Field | Detail |
|---|---|
| **What to show** | Split or stacked view: command output **and** Civil 3D profile (or plan + profile) after `HC_HGL` |
| **Data visible** | Command table: `HGL_US`, `HGL_DS`, `HGL_mid`, `h_m`, `d/D`, `SURCH`; labels on layer **HC-HGL**; optional **HC-HGL-PROFILE** polyline |
| **Tips** | Include HEC-22 losses (*Yes* at prompt). Profile view strongly preferred for this shot |

---

## Shot 5 — HC_ANALYZE Full-Network Analysis (v1.2.0)

**File name:** `05-hc-analyze.png`  
**Portal caption:** #5

| Field | Detail |
|---|---|
| **What to show** | Command line output after `HC_ANALYZE` on a drawing with pipe networks (and catchments if available) |
| **Data visible** | Multi-section summary: hydrology, routing, HGL, sediment, and/or compliance blocks; network name and pipe count header |
| **Tips** | Run on `C-STORM` for pipe/HGL sections; use a catchment-enabled DWG if compliance/sediment sections should appear. Capture enough scroll height to show at least two analysis sections |

---

## Shot 6 — HC_DETENTION Pond Routing (v1.2.0)

**File name:** `06-hc-detention.png`  
**Portal caption:** #6

| Field | Detail |
|---|---|
| **What to show** | Command line after `HC_DETENTION` with Modified Puls routing table |
| **Data visible** | Stage/storage steps, inflow hydrograph summary, outlet (orifice/weir) stages, peak attenuation or release rate |
| **Tips** | Accept default pond geometry or enter a simple trapezoidal basin; show at least 5–8 routing time steps in frame. May use synthetic inputs if drawing has no pond object |

---

## Shot 6a — HC_GVF Gradually Varied Flow (v1.2.0)

**File name:** `06a-hc-gvf.png`

| Field | Detail |
|---|---|
| **What to show** | Command line after `HC_GVF` with Standard Step water-surface profile table |
| **Data visible** | Station chainage, depth *y*, velocity *V*, friction slope *Sf*, energy grade line; trapezoidal channel inputs (bottom width, side slope, Manning *n*, bed slope) |
| **Caption** | *Gradually varied flow profile — Standard Step Method with Manning friction for trapezoidal open channels.* |
| **Tips** | Use a mild slope with subcritical flow; enter 3–5 stations at prompts. Show the step-by-step `CalcStep` trace in the output |

---

## Shot 7 — HC_PREPOST Peak Comparison (v1.2.0)

**File name:** `07-hc-prepost.png`  
**Portal caption:** #7

| Field | Detail |
|---|---|
| **What to show** | Command line after `HC_PREPOST` with pre- and post-development peak flows |
| **Data visible** | Pre-dev vs post-dev peak *Q* (cfs); multi-storm return periods (2/10/25/100-yr); state depth table if prompted |
| **Tips** | Use catchment objects or enter representative CN/area values at prompts. Show the peak-reduction summary line |

---

## Shot 8 — HC_REPORT in Browser

**File name:** `08-hc-report-browser.png`  
**Portal caption:** #8

| Field | Detail |
|---|---|
| **What to show** | Browser window displaying the HTML report from `HC_REPORT` |
| **Data visible** | Formula steps (`CalcStep` traces): label, value, units, equation for Manning and normal-depth HGL rows |
| **Tips** | Report path: `%USERPROFILE%\Documents\HydroComplete\`. Open latest HTML; scroll to HGL section showing normal-depth steps |

---

## Shot 9 — HC_ATLAS14 Live / Embedded IDF

**File name:** `09-hc-atlas14-idf.png`  
**Portal caption:** #9

| Field | Detail |
|---|---|
| **What to show** | Command line from `HC_ATLAS14` **or** `HC_RATIONAL` prompt showing IDF source (`live`, `cached live`, or `embedded`) |
| **Data visible** | Fitted *a*, *b*, *c* coefficients; preset name or lat/lon; note that live PFDS requires geolocation |
| **Tips** | With geo set, run `HC_RATIONAL` and accept `auto` at preset prompt to show live fetch. Without geo, show embedded preset list from `HC_ATLAS14` (~6–8 cities in frame) |

---

## Shot 10 — HC_ACTIVATE Pro Activation

**File name:** `10-hc-activate.png`  
**Portal caption:** #10

| Field | Detail |
|---|---|
| **What to show** | Command line after successful `HC_ACTIVATE` (email + `hc_live_*` beta token prompts) |
| **Data visible** | Activation success message; optional follow-up `HC_LICENSE` showing **Pro**, validation mode (`online` or `offline-stub`), and last validated timestamp |
| **Tips** | Capture activation prompt sequence or split frame: `HC_ACTIVATE` output + `HC_LICENSE` status. Redact email/token in store upload if needed |

---

## Optional supplementary shots

| File name | Command | When to capture |
|---|---|---|
| `04a-hc-capacity-write-labels.png` | `HC_CAPACITY_WRITE` | If Shot 3 needs plan-view overload labels |
| `04b-hc-hgl-profile-polyline.png` | `HC_HGL` (polyline Yes) | Magenta `Polyline3d` on `HC-HGL-PROFILE` |
| `05b-hc-routed-q.png` | `HC_CAPACITY` / `HC_HGL` | Drawing with catchments; routed Q column varies by reach |
| `08b-hc-report-pdf.png` | `HC_REPORT_PDF` | Pro license active; PDF viewer with formula steps |
| `11-hc-network-edit.png` | `HC_NETWORK_EDIT` | v1.2.0 override editor dialog |
| `12-hc-pump.png` | `HC_PUMP` | v1.2.0 pump duty-point table |

---

## File delivery

Place finished PNGs in:

```
dist/app-store/screenshots/
```

Update `SUBMISSION_CHECKLIST.md` checkboxes when complete. Upload PNGs to Autodesk Publisher when creating the listing. **Do not commit fake/placeholder screenshots** — this guide documents capture steps only.
