# Screenshot Capture Guide

Shot list for Michael to capture in **Civil 3D 2026** on a storm-sewer test drawing.
Target resolution: **1920×1080** (or Publisher portal minimum). Save as PNG.

**Recommended test drawing:** `C-STORM` (30 pipes, AutoCAD 2018-format DWG) — same drawing used for README validation.

---

## Before you shoot

1. Run `install.ps1` so the bundle auto-loads (banner shows v0.3.0).
2. Open the test DWG with pipe networks visible in plan.
3. Set a clean visual style: light background, pipe network color distinct, labels readable.
4. Hide unrelated palettes if they clutter the frame; keep the Civil 3D ribbon visible in at least one shot.
5. Scrub client names / addresses from title block if present.

---

## Shot 1 — HydroComplete Ribbon Tab

**File name:** `01-ribbon-tab.png`

| Field | Detail |
|---|---|
| **What to show** | Full Civil 3D window with **HydroComplete › Analysis** ribbon tab active |
| **Buttons visible** | Pipes, Write Labels, HGL, Rational, Report, Atlas 14, About (or whatever `RibbonBuilder.cs` exposes) |
| **Caption** | *HydroComplete Analysis commands on a dedicated ribbon tab — no memorizing HC_* names.* |
| **Tips** | Zoom drawing so a pipe network is visible behind the ribbon; confirms in-product integration |

---

## Shot 2 — HC_PIPES Command Output

**File name:** `02-hc-pipes-output.png`

| Field | Detail |
|---|---|
| **What to show** | Command line / text window after running `HC_PIPES` |
| **Data visible** | Pipe count (e.g. 30 pipes), diameter in feet (2.00 = 24″), Qfull (cfs), Vfull (ft/s), surcharge flags |
| **Caption** | *Manning full-barrel capacity and velocity for every pipe — computed from drawing geometry.* |
| **Tips** | Expand command history tall enough to show 3–4 representative pipes + summary line; optionally split view with plan |

---

## Shot 3 — HC_PIPES_WRITE Labels

**File name:** `03-hc-pipes-write-labels.png`

| Field | Detail |
|---|---|
| **What to show** | Plan view with MText labels at pipe midpoints on layer **HC-CAPACITY** |
| **Label content** | Qfull and Vfull values readable at zoom level (~1″=40′ or similar) |
| **Caption** | *Capacity results written back to the drawing on layer HC-CAPACITY.* |
| **Tips** | Run `HC_PIPES` first, then `HC_PIPES_WRITE`. Thaw/isolate `HC-CAPACITY` if needed. Pick a dense cluster of 5–8 pipes |

---

## Shot 4 — HC_HGL Labels

**File name:** `04-hc-hgl-labels.png`

| Field | Detail |
|---|---|
| **What to show** | Plan view (and profile if available) after `HC_HGL` with labels on layer **HC-HGL** |
| **Data visible** | HGL elevations at structures; optional design Q in command output |
| **Caption** | *Steady hydraulic grade line with HEC-22 junction and exit losses — labeled on HC-HGL.* |
| **Tips** | If catchments exist, run with catchment-driven Q; otherwise use prompted design Q. Include profile view in a split if Civil 3D profile shows HGL polyline |

---

## Shot 5 — HC_REPORT in Browser

**File name:** `05-hc-report-browser.png`

| Field | Detail |
|---|---|
| **What to show** | Browser window displaying the HTML report from `HC_REPORT` |
| **Data visible** | Formula steps (`CalcStep` traces): label, value, units, equation for Manning rows |
| **Caption** | *Formula-transparent HTML report — every number traceable to its equation and inputs.* |
| **Tips** | Report path: `%USERPROFILE%\Documents\HydroComplete\`. Open latest HTML; scroll to a step-by-step Manning section. Crop browser chrome + a sliver of Civil 3D behind optional |

---

## Shot 6 — HC_ATLAS14 Preset List

**File name:** `06-hc-atlas14-presets.png`

| Field | Detail |
|---|---|
| **What to show** | Command line output from `HC_ATLAS14` listing embedded presets |
| **Data visible** | City names, preset keys, IDF coefficients (*a*, *b*, *c*), duration notes (18 cities, 10-yr) |
| **Caption** | *18 embedded NOAA Atlas 14 IDF city curves — select by name in HC_RATIONAL.* |
| **Tips** | Widen command history to show ~6–8 cities; mention in caption that Rational accepts preset key instead of manual coefficients |

---

## Optional Shot 7 — HC_RATIONAL (if catchment test DWG available)

**File name:** `07-hc-rational-catchments.png`

| Field | Detail |
|---|---|
| **What to show** | `HC_RATIONAL` output with catchment areas, composite C, IDF intensity, peak Q |
| **Caption** | *Rational peak flow from catchment geometry and Atlas 14 intensity — no hand transcription.* |
| **Note** | Skip if test drawing has no catchments; README marks this validation as pending |

---

## File delivery

Place finished PNGs in:

```
dist/app-store/screenshots/
```

Update `SUBMISSION_CHECKLIST.md` checkboxes when complete. Upload PNGs to Autodesk Publisher when creating the listing.