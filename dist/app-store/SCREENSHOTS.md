# Screenshot Capture Guide

Shot list for Michael to capture in **Civil 3D 2026** on a storm-sewer test drawing.
Target resolution: **1920×1080** (or Publisher portal minimum). Save as PNG.

**Recommended test drawing:** `C-STORM` (30 pipes, AutoCAD 2018-format DWG) — same drawing used for README validation.

---

## Before you shoot

1. Run `install.ps1` so the bundle auto-loads (banner shows v0.8.0).
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
| **Buttons visible** | Pipe Capacity, Write Capacity, Design Capacity, Write Overload, HGL Profile, HTML Report, PDF Report, Rational Q, Atlas 14 IDF, Activate Pro, License, About (per `RibbonBuilder.cs`) |
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

## Shot 5 — HC_HGL Normal-Depth Profile + Labels

**File name:** `05-hc-hgl-profile.png`

| Field | Detail |
|---|---|
| **What to show** | Split or stacked view: command output **and** Civil 3D profile (or plan + profile) after `HC_HGL` |
| **Data visible** | Command table: `HGL_US`, `HGL_DS`, `HGL_mid`, `h_m`, `d/D`, `SURCH`; caption/subtitle mentions *normal depth*; labels on layer **HC-HGL** in plan or profile |
| **Caption** | *Steady hydraulic grade line at design Q using Manning normal depth — HEC-22 losses optional.* |
| **Tips** | Include HEC-22 losses (*Yes* at prompt). If catchments exist, use catchment-driven Q; otherwise prompted design Q. Profile view strongly preferred for this shot |

---

## Shot 5a — HC_HGL Profile Polyline (v0.5.0)

**File name:** `05a-hc-hgl-profile-polyline.png`

| Field | Detail |
|---|---|
| **What to show** | Civil 3D profile or 3D view after `HC_HGL` with **Draw HGL profile polyline** = **Yes** (default) |
| **Data visible** | Magenta `Polyline3d` on layer **HC-HGL-PROFILE**; vertices at pipe upstream/downstream ends with Z = computed HGL; command summary line *Wrote HGL profile polyline(s) for N network(s)* |
| **Caption** | *HGL profile polyline written to HC-HGL-PROFILE — 3D trace at pipe ends from computed upstream/downstream HGL.* |
| **Tips** | Thaw/isolate `HC-HGL-PROFILE`. Run `HC_HGL` after labels; accept default **Yes** at polyline prompt. Profile view best shows Z alignment with pipe inverts |

---

## Shot 5b — Routed Catchment Q (v0.5.0)

**File name:** `05b-hc-routed-q.png`

| Field | Detail |
|---|---|
| **What to show** | Command output from `HC_CAPACITY` or `HC_HGL` on a drawing **with catchments** after **Route catchment flows** = **Yes** |
| **Data visible** | Routed summary: *Routed catchment flows (outlet structures, N catchments, total Q=… cfs)*; per-catchment lines with `Q=… cfs -> struct …`; table header shows *routed Q, system total=… cfs*; per-pipe `Q(cfs)` column varies by reach |
| **Caption** | *Catchment flows routed through the pipe network — per-pipe design Q instead of a single uniform value.* |
| **Tips** | Requires catchment objects linked to structures. Accept default **Yes** at *Route catchment flows*; pick Atlas 14 preset or `auto`. Skip if test DWG has no catchments (see Shot 10) |

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
| **Tips** | Activate via `HC_ACTIVATE` first, or set `HYDROCOMPLETE_PRO=1` for dev capture. Crop viewer chrome; show one full calculation step block |

---

## Shot 8a — HC_ACTIVATE Pro Activation (v0.5.0)

**File name:** `08a-hc-activate.png`

| Field | Detail |
|---|---|
| **What to show** | Command line after successful `HC_ACTIVATE` (email + `hc_live_*` beta token prompts) |
| **Data visible** | Activation success message; license written to `%APPDATA%\HydroComplete\license.json`; optional follow-up `HC_LICENSE` showing **Pro**, validation mode (`online` or `offline-stub`), and last validated timestamp |
| **Caption** | *Activate Pro with email and beta token — online validation or offline stub unlocks PDF export.* |
| **Tips** | Capture activation prompt sequence or split frame: `HC_ACTIVATE` output + `HC_LICENSE` status. Redact email/token in store upload if needed |

---

## Shot 9 — HC_ATLAS14 Live / Embedded IDF

**File name:** `09-hc-atlas14-idf.png`

| Field | Detail |
|---|---|
| **What to show** | Command line from `HC_ATLAS14` **or** `HC_RATIONAL` prompt showing IDF source (`live`, `cached live`, or `embedded`) |
| **Data visible** | Fitted *a*, *b*, *c* coefficients; preset name or lat/lon; note that live PFDS requires geolocation |
| **Caption** | *Live NOAA Atlas 14 PFDS from drawing location — 25 embedded city presets when offline.* |
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