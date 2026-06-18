# Screenshot Capture Guide

Shot list for Michael to capture in **Civil 3D 2026** on a storm-sewer test drawing.
Target resolution: **1920×1080** (or Publisher portal minimum). Save as PNG.

**Recommended test drawing:** `C-STORM` (30 pipes, AutoCAD 2018-format DWG) — same drawing used for README validation.

---

## Before you shoot

1. Run `install.ps1` so the bundle auto-loads (banner shows v0.4.0).
2. Open the test DWG with pipe networks visible in plan.
3. Set a clean visual style: light background, pipe network color distinct, labels readable.
4. Hide unrelated palettes if they clutter the frame; keep the Civil 3D ribbon visible in at least one shot.
5. Scrub client names / addresses from title block if present.
6. For live Atlas 14 shots, set `GEOGRAPHICLOCATION` on the drawing (or use a geo-referenced test DWG).

---

## Shot 1 — HydroComplete Ribbon Tab

**File name:** `01-ribbon-tab.png`

| Field | Detail |
|---|---|
| **What to show** | Full Civil 3D window with **HydroComplete › Analysis** ribbon tab active |
| **Buttons visible** | Pipes, Write Labels, Design Capacity, Write Overload, HGL, Rational, Report, PDF Report, Atlas 14, About (per `RibbonBuilder.cs`) |
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

## Shot 3 — HC_CAPACITY Overload Check

**File name:** `03-hc-capacity-output.png`

| Field | Detail |
|---|---|
| **What to show** | Command line output after `HC_CAPACITY` with a design *Q* that surcharges at least one pipe |
| **Data visible** | Header row (`Q_full`, `Q_des`, `Q_des/Q`, `d/D`, `SURCH`); mix of OK and `*` surcharged pipes; summary count of overloaded pipes |
| **Caption** | *Design flow vs full-barrel capacity — surcharge flags and d/D for every pipe at design Q.* |
| **Tips** | Use a design *Q* high enough to flag 2–3 pipes; show the summary line *N pipe(s) surcharged* |

---

## Shot 4 — HC_CAPACITY_WRITE Overload Labels

**File name:** `04-hc-capacity-write-labels.png`

| Field | Detail |
|---|---|
| **What to show** | Plan view with MText labels on layer **HC-CAPACITY** after `HC_CAPACITY_WRITE` (overload-only mode) |
| **Label content** | Q_des/Q_full or surcharge notation readable at zoom level (~1″=40′ or similar) |
| **Caption** | *Overload labels written back to the drawing — only surcharged pipes when overload-only mode is on.* |
| **Tips** | Run `HC_CAPACITY` first, then `HC_CAPACITY_WRITE` and accept default *Yes* for overloaded-only. Thaw/isolate `HC-CAPACITY` |

---

## Shot 5 — HC_HGL Normal-Depth Profile

**File name:** `05-hc-hgl-profile.png`

| Field | Detail |
|---|---|
| **What to show** | Split or stacked view: command output **and** Civil 3D profile (or plan + profile) after `HC_HGL` |
| **Data visible** | Command table: `HGL_US`, `HGL_DS`, `HGL_mid`, `h_m`, `d/D`, `SURCH`; caption/subtitle mentions *normal depth*; labels on layer **HC-HGL** in plan or profile |
| **Caption** | *Steady hydraulic grade line at design Q using Manning normal depth — HEC-22 losses optional.* |
| **Tips** | Include HEC-22 losses (*Yes* at prompt). If catchments exist, use catchment-driven Q; otherwise prompted design Q. Profile view strongly preferred for this shot |

---

## Shot 6 — HC_PIPES_WRITE Labels (optional legacy)

**File name:** `06-hc-pipes-write-labels.png`

| Field | Detail |
|---|---|
| **What to show** | Plan view with MText labels at pipe midpoints on layer **HC-CAPACITY** after `HC_PIPES_WRITE` |
| **Label content** | Qfull and Vfull values readable at zoom level |
| **Caption** | *Full-barrel capacity results on layer HC-CAPACITY — complements design-capacity overload labels.* |
| **Tips** | Optional if Shot 4 already shows HC-CAPACITY clearly; run `HC_PIPES` then `HC_PIPES_WRITE` |

---

## Shot 7 — HC_REPORT in Browser

**File name:** `07-hc-report-browser.png`

| Field | Detail |
|---|---|
| **What to show** | Browser window displaying the HTML report from `HC_REPORT` |
| **Data visible** | Formula steps (`CalcStep` traces): label, value, units, equation for Manning and normal-depth HGL rows |
| **Caption** | *Formula-transparent HTML report — every number traceable to its equation and inputs.* |
| **Tips** | Report path: `%USERPROFILE%\Documents\HydroComplete\`. Open latest HTML; scroll to HGL section showing normal-depth steps |

---

## Shot 8 — HC_REPORT_PDF (Pro)

**File name:** `08-hc-report-pdf.png`

| Field | Detail |
|---|---|
| **What to show** | PDF viewer (or print preview) of the report from `HC_REPORT_PDF` with Pro license active |
| **Data visible** | Same step-by-step traces as HTML; HydroComplete branding; Manning + HGL sections |
| **Caption** | *Sealable PDF export — Pro unlocks formula-transparent reports for review packages.* |
| **Tips** | Set `HYDROCOMPLETE_PRO=1` for dev capture, or activate via `HC_LICENSE`. Crop viewer chrome; show one full calculation step block |

---

## Shot 9 — HC_ATLAS14 Live / Embedded IDF

**File name:** `09-hc-atlas14-idf.png`

| Field | Detail |
|---|---|
| **What to show** | Command line from `HC_ATLAS14` **or** `HC_RATIONAL` prompt showing IDF source (`live`, `cached live`, or `embedded`) |
| **Data visible** | Fitted *a*, *b*, *c* coefficients; preset name or lat/lon; note that live PFDS requires geolocation |
| **Caption** | *Live NOAA Atlas 14 PFDS from drawing location — 18 embedded city presets when offline.* |
| **Tips** | With geo set, run `HC_RATIONAL` and accept `auto` at preset prompt to show live fetch. Without geo, show embedded preset list from `HC_ATLAS14` (~6–8 cities in frame) |

---

## Optional Shot 10 — HC_RATIONAL (if catchment test DWG available)

**File name:** `10-hc-rational-catchments.png`

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