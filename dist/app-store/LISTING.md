# Autodesk App Store â€” Listing Copy

**Product:** HydroComplete for Civil 3D  
**Version at submission:** 1.7.2
**Last updated:** 2026-06-23

---

## Title

**HydroComplete for Civil 3D**

---

## Category

**Civil Engineering** (Autodesk App Store taxonomy — stormwater hydrology/hydraulics analysis for Civil 3D pipe networks)

---

## Short Description (â‰¤ 80 characters)

```
Civil 3D storm sewers â€” capacity overload, normal-depth HGL, live Atlas 14 IDF.
```

*(78 characters)*

---

## Long Description

Stop re-typing catchment areas, pipe runs, and invert elevations into a separate hydraulics model. **HydroComplete for Civil 3D** reads your pipe networks and catchments straight from the open drawing, runs stormwater hydrology and hydraulics on audited public-domain methods, and writes results back into the model â€” with every formula shown line by line.

Built by a practicing PE for the everyday sizing and reporting work Civil 3D engineers do constantly. No black boxes. No manual transcription.

### What it does

- **Design capacity check** â€” Compare design *Q* to full-barrel Manning capacity for every pipe; see *Q*<sub>des</sub>/*Q*<sub>full</sub>, *d*/*D*, and surcharge flags (`HC_CAPACITY`, `HC_CAPACITY_WRITE`). When catchments exist, optionally **route catchment flows** through the pipe network so each pipe gets its tributary *Q* instead of a single uniform value.
- **Manning capacity** â€” Full-barrel flow, normal depth, peak velocity, and surcharge flags for every pipe in every pipe network (`HC_PIPES`, `HC_PIPES_WRITE`).
- **Normal-depth hydraulic grade line** â€” Steady HGL at design *Q* using Manning normal depth (partial flow + velocity head), with optional HEC-22 junction and exit minor losses; plan labels on layer `HC-HGL`; optional **3D profile polyline** on layer `HC-HGL-PROFILE` at pipe upstream/downstream ends (`HC_HGL`). Routed catchment *Q* supported when catchments are present.
- **Rational peak flows** â€” `Q = CÂ·iÂ·A` from catchment geometry with composite runoff coefficients (`HC_RATIONAL`).
- **Live NOAA Atlas 14 IDF** â€” PFDS intensityâ€“durationâ€“frequency fetch from drawing geolocation (30-day cache); 25 embedded US city presets as offline fallback (`HC_ATLAS14`, `HC_RATIONAL`).
- **Formula-transparent reports** â€” HTML export with **KaTeX-rendered equations** and step-by-step calculation traces to `Documents\HydroComplete\` (`HC_REPORT`); sealable PDF export with Pro (`HC_REPORT_PDF`). Reports reflect routed per-pipe *Q* when catchment routing is used.
- **Network schematic export** â€” `HC_NETWORK_DIAGRAM` writes an HTML/SVG pipe-network diagram from plan topology for review packages and submittals.
- **Live SSURGO soils** â€” `HC_SOIL` queries USDA SSURGO by drawing geolocation (30-day cache) with regional fallback; surfaces hydrologic soil group, K-factor, and BMP suitability hints.
- **Pro activation** â€” Activate with email and beta token (`hc_live_*`) via online validation or offline stub; `HC_LICENSE` shows tier and last check (`HC_ACTIVATE`, `HC_LICENSE`).
- **Ribbon integration** â€” **HydroComplete â€º Analysis** tab exposes the same commands as the command line.

### Why HydroComplete

| | Typical separate-tool workflow | HydroComplete connector |
|---|---|---|
| Reads geometry from the drawing | Export / re-key | Direct from Civil 3D objects |
| Shows the equation behind every number | No | Yes â€” line by line |
| Results back in the drawing | Manual annotation | MText labels on dedicated layers |
| IDF curves | Manual lookup | Live PFDS + embedded Atlas 14 presets |
| Overload flags at design *Q* | Separate spreadsheet | `HC_CAPACITY` + optional labels |

Civil 3D ships Storm and Sanitary Analysis, and it is capable. HydroComplete is not trying to replace a full hydraulic-modeling suite â€” it targets routine storm-sewer sizing, HGL checks, and defensible reporting where the friction is transcription and reviewability.

### Methods (public domain)

Rational method Â· Kirpich & NRCS time of concentration Â· IDF intensity `i = a/(t+b)^c` Â· Manning circular flow (full barrel + normal depth) Â· HEC-22 minor losses

Every engine result carries a `Steps` trace (label, value, units, formula) â€” the same "show your work" data behind HydroComplete's web app and sealable reports.

Learn more: [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d)

### New in v1.7.1

- **`HC_DAG`** *(net8, Civil 3D 2025/2026 only)* — **Visual model builder**: drag-and-drop stormwater node DAG editor in a dockable WebView2 panel. Connects hydrology → hydraulics → water quality engines in one visual pipeline. 20 node types (Catchment, SCS CN, Rational, Unit Hydro, Manning Pipe/Channel, Detention Pond, WQV, BMP Sizing, Treatment Train, RUSLE, Sediment Basin, GVF Profile, Continuous Simulation, and more). Features: palette search, Ctrl+Z/Y undo/redo, multi-select with rubber-band, group copy/paste, auto-layout, snap-to-grid, inline node label editing (F2 / double-click), minimap, Config + Chart inspector tabs, HTML + SVG export, 5 pre-built templates, localStorage autosave, auto-populates from drawing catchments and pipe networks.
- **`HC_DAG_SAVE` / `HC_DAG_LOAD`** — Persist the DAG alongside the drawing as `<drawing>.hcdag`; `HC_DAG_LOAD` reopens the editor and restores the model.
- **`HC_LOSS`** — Incremental loss method (Green-Ampt / Horton / SCS CN) on a SCS Type II design storm; reports per-step excess and peak intensity.
- **`HC_CONTINUOUS`** — Multi-year daily continuous simulation (17 US cities, Hargreaves ET, soil-moisture bucket, moisture-adjusted SCS CN, land-use-specific pollutant loads).
- **`HC_WQ_DIAGRAM`** — HTML/SVG BMP treatment-train node diagram with per-pollutant removal labels and overall efficiency summary.
- **52 `HC_*` commands** total, registered for Civil 3D 2024 (R24.3), 2025 (R25.0), and 2026 (R25.1).

### New in v1.4.0

- **`HC_NETWORK_DIAGRAM`** â€” Export an HTML/SVG pipe-network schematic from plan topology (structures, pipes, flow direction) to `Documents\HydroComplete\` for review and submittal packages.
- **KaTeX HTML reports** â€” `HC_REPORT` embeds KaTeX so Manning, IDF, and HGL formulas render as typeset equations in the browser (offline CDN with graceful fallback).
- **Live SSURGO API** â€” `HC_SOIL` fetches map-unit data from USDA Soil Data Access by lat/lon (30-day cache); regional NRCS fallback when offline or out of coverage.
- **Online Pro activation** â€” `HC_ACTIVATE` validates `hc_live_*` tokens against the production licensing API (`hydrocomplete.com/api/licensing/validate`).
- **Manifest** â€” All **46** `HC_*` commands registered for Civil 3D 2024 (R24.3), 2025 (R25.0), and 2026 (R25.1).

### Also in v1.3.0

- **`HC_ROUTE_HYDRO`** â€” Route catchment hydrographs through the pipe network (TR-20 lag â†’ SCS unit hydrograph â†’ Kahn topological routing â†’ junction superposition; optional Muskingum-Cunge on long reaches). Exports CSV to `Documents\HydroComplete\`.
- **Civil 3D 2024 support** â€” `net48` build staged at `Contents/net48/` with R24.3 `ComponentEntry` in `PackageContents.xml` (build via `scripts/build-net48.ps1`).

### Also in v1.2.0

- **`HC_PUMP`** â€” Pump station duty-point check (system head vs default pump curve).
- **`HC_NETWORK_EDIT`** â€” Interactive pipe override editor (design Q, Manning *n*); saved per drawing in `%APPDATA%\HydroComplete\overrides\`.
- **`HC_COST`** â€” Pipe cost roll-up from diameter catalog ($/LF).
- **`HC_BACKGROUND`** â€” Attach georeferenced raster image on layer `HC-BACKGROUND`.
- **Modal UI** â€” `HC_PROFILE` and `HC_INLETS` open WPF option dialogs (scales, HGL, tailwater, inlet type).
- **`HC_GVF`** â€” Gradually varied flow water surface profile (Standard Step Method, trapezoidal channel).

### Also in v1.1.0

- **`HC_PROFILE_DXF`** â€” Export chainage profile (invert, crown, optional HGL) to ASCII DXF for external CAD review.
- **App Store manifest** â€” Registers `HC_PROFILE`, `HC_PROFILE_DXF`, `HC_TC`, `HC_LANDXML`, and `HC_LANDXML_IMPORT`.

### Also in v1.0.0

- **`HC_LANDXML_IMPORT` write** â€” Optional Yes/No prompt creates Civil 3D pipe-network geometry from LandXML (structures, pipes, catalog parts).
- **Box/arch pipe read** â€” `HC_PIPES` and `HC_LANDXML` export detect rectangular and arch cross-sections from Civil 3D parts.
- **LandXML box/arch** â€” Reader/writer preserve `BoxPipe` and `ArchPipe` shape, width, and height.

### Also in v0.9.0

- **`HC_PROFILE`** â€” Chainage profile plot (invert, crown, optional HGL on `HC-PROFILE-*` layers).
- **`HC_LANDXML_IMPORT`** â€” Read LandXML 1.2 and compare pipe counts to the active drawing.
- **Arch conduit Manning** â€” Pipe-arch partial flow, capacity, and normal depth in engine.
- **`Manning.Capacity` / `NormalDepth`** â€” Shape dispatch for circular, box, and arch pipes.

### Also in v0.8.0

- **`HC_TC`** â€” TR-55 segmented time-of-concentration worksheet (sheet / shallow / channel segments).
- **HEC-22 inlets** â€” `HC_INLETS` supports grate-on-grade, sag, and curb-opening inlet types.
- **Box conduit Manning** â€” Rectangular pipe hydraulics (partial flow, normal depth) in engine.
- **`HC_LANDXML`** â€” Export pipe network geometry and hydraulics to LandXML 1.2.
- **State compliance** â€” `HC_REVIEW`, `HC_SCS`, `HC_SEDIMENT`, `HC_WQV` enabled; detention/BMP commands (`HC_DETENTION`, `HC_BMP_SIZE`, `HC_WQ_TRAIN`, `HC_SEDIMENT_BASIN`).
- **Command registration fix** â€” All `HC_*` command classes registered for reliable auto-load.

### Also in v0.7.0

- **`HC_VALIDATE`** â€” Design-criteria review (slope, capacity, velocity, cover, HGL flooding).
- **`HC_SIZE`** â€” Standard catalog pipe sizing.
- **`HC_MULTIRP`** â€” Per-pipe Q and d/D for 2/10/25/100-yr storms.
- **HGL junction losses** â€” Optional momentum junction + bend losses in `HC_HGL`.

### New in v0.6.1

- **Tailwater-controlled HGL** â€” `HC_HGL` anchors at outfall tailwater and steps upstream (correct storm-sewer gradeline direction); tailwater elevation prompt (default = outfall invert).
- **Confluence flow routing fix** â€” Catchment Q accumulation uses topological (Kahn) order so trunk pipes below junctions carry the full tributary sum.
- **License gate hardening** â€” Server `valid:false` now denies activation; offline stub only when the server is unreachable.
- **Atlas 14 reliability** â€” Fixed NOAA PFDS URL; 8 s fetch timeout; explicit warning when geolocation falls back outside Atlas 14 coverage (e.g. Pacific NW).
- **Atlas 14 Volume 12** â€” Idaho/Montana embedded presets added (25 presets total).

### Also in v0.6.0

- **`HC_NETWORK`** â€” Per-network pipe/structure summary table.
- **HGL profile polyline** â€” `Polyline3d` on `HC-HGL-PROFILE` after `HC_HGL` labels.
- **Catchment Q routing** â€” Per-pipe design *Q* through pipe topology for capacity, HGL, and reports.
- **Pro activation** â€” `HC_ACTIVATE` online + offline stub; `HC_REPORT_PDF` gated on Pro.

---

## Command reference (52 commands â€” v1.7.2)

All commands print formula-transparent `CalcStep` traces where applicable. Type `HC_ABOUT` in Civil 3D for the live list.

### Network & capacity

| Command | Description |
|---|---|
| `HC_NETWORK` | Per-network summary (pipes, length, inverts, diameters, structures) |
| `HC_PIPES` | Manning capacity of every pipe-network pipe (circular, box, arch) |
| `HC_PIPES_WRITE` | Label Q<sub>full</sub>/V<sub>full</sub> on layer `HC-CAPACITY` |
| `HC_CAPACITY` | Design *Q* vs *Q*<sub>full</sub> check (*d*/*D*, surcharge flag) |
| `HC_CAPACITY_WRITE` | Label overloaded pipes on layer `HC-CAPACITY` |
| `HC_SIZE` | Standard catalog pipe sizing (velocity, % full) |
| `HC_VALIDATE` | Design-criteria review (slope, capacity, velocity, cover, HGL) |
| `HC_MULTIRP` | Per-pipe *Q* and *d*/*D* for 2/10/25/100-yr return periods |
| `HC_NETWORK_EDIT` | Edit pipe *Q* and Manning *n* overrides (saved per drawing) |
| `HC_NETWORK_DIAGRAM` | Export HTML/SVG pipe-network schematic from plan topology |
| `HC_COST` | Pipe cost roll-up from diameter catalog ($/LF) |

### Hydrology & analysis

| Command | Description |
|---|---|
| `HC_RATIONAL` | Rational *Q* from catchments + NOAA Atlas 14 IDF presets |
| `HC_TC` | TR-55 segmented time-of-concentration worksheet |
| `HC_SCS` | SCS CN runoff from catchments |
| `HC_UNIT_HYDRO` | SCS unit hydrograph table output |
| `HC_HYDROGRAPH` | Synthetic hydrograph ordinates (SCS, Clark, Snyder) |
| `HC_ROUTE_HYDRO` | Route catchment hydrographs through pipe network (lag + junction superposition) |
| `HC_ATLAS14` | List Atlas 14 IDF presets + live PFDS fetch info |
| HC_LOSS | Incremental loss method (Green-Ampt / Horton / SCS CN) on a SCS Type II design storm; per-step excess and peak intensity |
| HC_CONTINUOUS | Multi-year daily continuous simulation (17 US cities, Hargreaves ET, soil-moisture bucket, moisture-adjusted SCS CN, pollutant loads) |
| `HC_ANALYZE` | Full-network analysis (hydrology, routing, HGL, sediment, compliance) |
| `HC_PREPOST` | Pre/post-development peak comparison (SCS UH, multi-storm state depths) |
| `HC_OPTIMIZE` | BMP treatment-train cost optimizer (top 3 chains) |

### Hydraulics & structures

| Command | Description |
|---|---|
| `HC_HGL` | Steady HGL (normal depth) + HEC-22/momentum/bend losses + `HC-HGL` labels + plan profile |
| `HC_INLETS` | HEC-22 inlet check (grate / sag / curb opening) â€” modal options dialog |
| `HC_CULVERT` | Culvert headwater (FHWA HDS-5 inlet/outlet control) |
| `HC_GVF` | Gradually varied flow profile (Standard Step, trapezoidal channel) |
| `HC_PUMP` | Pump station duty-point check (curve vs system head) |
| `HC_PROFILE` | Chainage profile plot (invert, crown, optional HGL) â€” modal options dialog |
| `HC_PROFILE_DXF` | Export chainage profile to DXF (invert, crown, optional HGL) |

### Stormwater quality, BMPs & detention

| Command | Description |
|---|---|
| `HC_WQV` | Water quality volume calculation |
| `HC_DETENTION` | Detention pond routing (Modified Puls, SCS UH inflow, orifice/weir outlets) |
| `HC_BMP_SIZE` | WQV-based BMP sizing (bioretention, wet pond, sand filter, swale) |
| `HC_WQ_TRAIN` | BMP treatment train with EMC pollutant loads from catchments |
| `HC_SEDIMENT` | RUSLE/MUSLE soil loss from catchments |
| `HC_SEDIMENT_BASIN` | Sediment basin design from peak *Q* (NCDEQ surface-area method) |
| `HC_BIORETENTION` | Bioretention routing with underdrain/outlet |
| `HC_WETLAND` | Wetland detention routing |
| `HC_SOIL` | Live SSURGO lookup by drawing geo or map-unit name; hydrologic soil group, K-factor, BMP hints |

### Compliance, exchange & reporting

| Command | Description |
|---|---|
| `HC_REVIEW` | Design review + state regulatory compliance check |
| `HC_LANDXML` | Export pipe network to LandXML 1.2 |
| `HC_LANDXML_IMPORT` | Import LandXML 1.2 (preview + optional write to drawing) |
| `HC_REPORT` | Export formula-transparent HTML Manning + HGL report (free) |
| `HC_REPORT_PDF` | Export formula-transparent PDF Manning + HGL report (Pro) |
| `HC_BACKGROUND` | Attach georeferenced raster on `HC-BACKGROUND` layer |

### Visual model builder (net8, Civil 3D 2025/2026)

| Command | Description |
|---|---|
| `HC_DAG` | Dockable drag-and-drop stormwater node DAG editor (WebView2 panel); 20 node types, 5 templates, undo/redo, multi-select, auto-layout, charts, SVG export |
| `HC_DAG_SAVE` | Save the current DAG alongside the drawing as `<drawing>.hcdag` |
| `HC_DAG_LOAD` | Load a `.hcdag` file and reopen the DAG editor |
### License & help

| Command | Description |
|---|---|
| `HC_ACTIVATE` | Activate Pro with email + beta token (`hc_live_*`) |
| `HC_LICENSE` | Show Free/Pro license status and activation info |
| `HC_ABOUT` | Command list and version banner |

---

## Key Features (bullet list for store UI)

- **52 HC_* commands** â€” Network capacity, hydrology, routed hydrographs, HGL, GVF, detention, BMPs, compliance, LandXML, network diagrams, and reports in one auto-load bundle
- **Design capacity overload check** â€” Design *Q* vs *Q*<sub>full</sub> with *d*/*D* and surcharge flags; optional routed catchment *Q* per pipe; label overloaded pipes on `HC-CAPACITY`
- **Full-network analysis** â€” `HC_ANALYZE` combines hydrology, routing, HGL, sediment, and compliance in one pass
- **Detention & BMP suite** â€” `HC_DETENTION`, `HC_BMP_SIZE`, `HC_WQ_TRAIN`, `HC_PREPOST`, `HC_OPTIMIZE` for stormwater quality and peak control
- **Manning pipe capacity** â€” Full-flow *Q*, velocity, normal depth, and surcharge flags for all pipes in pipe networks
- **Gradually varied flow** â€” `HC_GVF` Standard Step water surface profile for trapezoidal open channels
- **Normal-depth HGL** â€” Steady profile at design *Q* with HEC-22 junction/exit losses; plan labels on `HC-HGL`; 3D profile polyline on `HC-HGL-PROFILE`
- **Catchment Q routing** â€” Route Rational peak flows through pipe networks for per-reach design *Q* in capacity, HGL, and reports
- **Routed hydrographs** â€” `HC_ROUTE_HYDRO` routes full catchment hydrographs with junction superposition and optional Muskingum-Cunge
- **Rational method** â€” Peak runoff from catchment areas with composite *C* and IDF intensity
- **Live NOAA Atlas 14 IDF** â€” PFDS fetch from drawing geolocation; 25 embedded city presets offline
- **Formula transparency** â€” Every number traceable to its equation and inputs; KaTeX-rendered HTML report export (free)
- **Network diagram export** â€” `HC_NETWORK_DIAGRAM` HTML/SVG schematic for review packages
- **Live SSURGO soils** â€” `HC_SOIL` with USDA API fetch, cache, and regional fallback
- **PDF Pro export** â€” Sealable formula-transparent Manning + HGL PDF (`HC_REPORT_PDF`)
- **Pro activation** â€” Email + beta token activation with online or offline validation (`HC_ACTIVATE`, `HC_LICENSE`)
- **Drawing write-back** â€” MText capacity and HGL labels on `HC-CAPACITY` and `HC-HGL`; HGL profile polylines on `HC-HGL-PROFILE`
- **Auto-load bundle** â€” One-time install; loads on Civil 3D startup (no NETLOAD for end users)
- **Audited engine** â€” Shared `HydroComplete.Engine` assembly unit-tested off the CAD machine

---

## Supported Products

| Product | Series | Runtime |
|---|---|---|
| **Autodesk Civil 3D 2024** | R24.3 | .NET Framework 4.8 (`net48`) |
| **Autodesk Civil 3D 2025** | R25.0 | .NET 8 (`net8.0-windows`) |
| **Autodesk Civil 3D 2026** | R25.1 | .NET 8 (`net8.0-windows`) |

> **Note:** The portable engine targets `netstandard2.0` so the same hydraulics core ships across all three Civil 3D versions. Civil 3D 2024 loads `Contents/net48/` DLLs; 2025+ load `Contents/` net8 DLLs per `PackageContents.xml` `SeriesMin`/`SeriesMax`.

---

## System Requirements

| Requirement | Detail |
|---|---|
| **Operating system** | Windows 10 or Windows 11 (64-bit) |
| **Host application** | Autodesk Civil 3D 2024, 2025, or 2026 (desktop application â€” not `accoreconsole` or plain AutoCAD) |
| **.NET runtime** | .NET Framework 4.8 (Civil 3D 2024) or .NET 8 (Civil 3D 2025/2026 â€” included with host) |
| **Drawing content** | Civil 3D pipe network objects for `HC_PIPES` / `HC_HGL` / `HC_CAPACITY`; catchment objects for `HC_RATIONAL` with catchment-driven flows; `GEOGRAPHICLOCATION` for live Atlas 14 PFDS |
| **Disk space** | &lt; 5 MB for the application bundle |
| **Network** | Not required for core analysis (offline-capable). Live Atlas 14 PFDS and Pro license validation require internet when used. |

---

## Pricing

**Freemium** — Free core analysis; Pro upgrade for sealable PDF export.

| Tier | App Store price | Includes |
|---|---|---|
| **Free** | $0 | Manning capacity, design overload checks, normal-depth HGL (labels + profile polyline), routed catchment *Q*, 52 `HC_*` commands, formula-transparent HTML reports (`HC_REPORT`) |
| **Pro** | **$199/year** | Everything in Free, plus sealable PDF reports (`HC_REPORT_PDF`). Purchase at [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d) (Stripe); activate in Civil 3D with `HC_ACTIVATE` (email + `hc_live_*` token). |

- **Portal setting:** List as **Free** with in-app Pro upgrade (matches Autodesk freemium + external checkout flow).
- **Beta / waitlist:** Introductory tokens may be issued manually before public store listing; same `HC_ACTIVATE` flow.

---

## Keywords (search / SEO)

```
stormwater, Civil 3D, hydraulics, hydrology, Manning, Rational method, pipe network,
storm sewer, HGL, hydraulic grade line, HEC-22, IDF, NOAA Atlas 14, intensity duration frequency,
pipe capacity, surcharge, overload, normal depth, runoff, catchment, time of concentration,
Kirpich, NRCS, PE report, formula transparency, storm drain, sanitary sewer, AutoCAD plugin,
Civil 3D add-in, PDF report
```

---

## Publisher Contact (matches `PackageContents.xml`)

| Field | Value |
|---|---|
| **Company** | HydroComplete |
| **Product URL** | https://hydrocomplete.com/civil3d |
| **Support email** | support@hydrocomplete.com |
| **Privacy policy** | https://hydrocomplete.com/privacy.html |

---

## Release Notes (Publisher portal — v1.7.2)

Paste into the **What's New** / release-notes field at upload. Trim to portal character limit if needed.

```
HydroComplete for Civil 3D v1.7.2 — stormwater hydrology and hydraulics inside your Civil 3D drawing.

NEW IN v1.7.x
• HC_DAG visual model builder (Civil 3D 2025/2026) — drag-and-drop stormwater DAG editor with 20 node types, undo/redo, templates, charts, and SVG export
• HC_DAG_SAVE / HC_DAG_LOAD — persist models as <drawing>.hcdag beside the DWG
• HC_LOSS — incremental loss method on SCS Type II design storms
• HC_CONTINUOUS — multi-year daily continuous simulation with moisture-adjusted SCS CN
• HC_WQ_DIAGRAM — BMP treatment-train SVG with per-pollutant removal labels
• 52 HC_* commands registered for Civil 3D 2024, 2025, and 2026

CORE CAPABILITIES
• Manning pipe capacity, design overload checks, and normal-depth HGL with HEC-22 losses
• Catchment Q routing through pipe networks for per-reach design flows
• Live NOAA Atlas 14 IDF from drawing geolocation; 25 embedded US city presets offline
• Formula-transparent HTML reports (KaTeX equations); Pro unlocks sealable PDF export
• Network schematic export (HC_NETWORK_DIAGRAM), LandXML exchange, SSURGO soils lookup

SUPPORTED PRODUCTS
• Autodesk Civil 3D 2024 (R24.3), 2025 (R25.0), and 2026 (R25.1)
• Windows 10/11 64-bit; auto-load bundle — no NETLOAD for end users

PRICING
• Free: core analysis and HTML reports
• Pro ($199/year via hydrocomplete.com/civil3d): sealable PDF reports — activate with HC_ACTIVATE
```

---

## Legal / Trademark Notice (include in store listing footer)

Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk, Inc. HydroComplete is an independent product and is not affiliated with or endorsed by Autodesk.

