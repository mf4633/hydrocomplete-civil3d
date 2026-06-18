# Autodesk App Store — Listing Copy

**Product:** HydroComplete for Civil 3D  
**Version at submission:** 0.5.0  
**Last updated:** 2026-06-17

---

## Title

**HydroComplete for Civil 3D**

---

## Short Description (≤ 80 characters)

```
Civil 3D storm sewers — capacity overload, normal-depth HGL, live Atlas 14 IDF.
```

*(78 characters)*

---

## Long Description

Stop re-typing catchment areas, pipe runs, and invert elevations into a separate hydraulics model. **HydroComplete for Civil 3D** reads your pipe networks and catchments straight from the open drawing, runs stormwater hydrology and hydraulics on audited public-domain methods, and writes results back into the model — with every formula shown line by line.

Built by a practicing PE for the everyday sizing and reporting work Civil 3D engineers do constantly. No black boxes. No manual transcription.

### What it does

- **Design capacity check** — Compare design *Q* to full-barrel Manning capacity for every pipe; see *Q*<sub>des</sub>/*Q*<sub>full</sub>, *d*/*D*, and surcharge flags (`HC_CAPACITY`, `HC_CAPACITY_WRITE`). When catchments exist, optionally **route catchment flows** through the pipe network so each pipe gets its tributary *Q* instead of a single uniform value.
- **Manning capacity** — Full-barrel flow, normal depth, peak velocity, and surcharge flags for every pipe in every pipe network (`HC_PIPES`, `HC_PIPES_WRITE`).
- **Normal-depth hydraulic grade line** — Steady HGL at design *Q* using Manning normal depth (partial flow + velocity head), with optional HEC-22 junction and exit minor losses; plan labels on layer `HC-HGL`; optional **3D profile polyline** on layer `HC-HGL-PROFILE` at pipe upstream/downstream ends (`HC_HGL`). Routed catchment *Q* supported when catchments are present.
- **Rational peak flows** — `Q = C·i·A` from catchment geometry with composite runoff coefficients (`HC_RATIONAL`).
- **Live NOAA Atlas 14 IDF** — PFDS intensity–duration–frequency fetch from drawing geolocation (30-day cache); 18 embedded US city presets as offline fallback (`HC_ATLAS14`, `HC_RATIONAL`).
- **Formula-transparent reports** — HTML export with step-by-step calculation traces to `Documents\HydroComplete\` (`HC_REPORT`); sealable PDF export with Pro (`HC_REPORT_PDF`). Reports reflect routed per-pipe *Q* when catchment routing is used.
- **Pro activation** — Activate with email and beta token (`hc_live_*`) via online validation or offline stub; `HC_LICENSE` shows tier and last check (`HC_ACTIVATE`, `HC_LICENSE`).
- **Ribbon integration** — **HydroComplete › Analysis** tab exposes the same commands as the command line.

### Why HydroComplete

| | Typical separate-tool workflow | HydroComplete connector |
|---|---|---|
| Reads geometry from the drawing | Export / re-key | Direct from Civil 3D objects |
| Shows the equation behind every number | No | Yes — line by line |
| Results back in the drawing | Manual annotation | MText labels on dedicated layers |
| IDF curves | Manual lookup | Live PFDS + embedded Atlas 14 presets |
| Overload flags at design *Q* | Separate spreadsheet | `HC_CAPACITY` + optional labels |

Civil 3D ships Storm and Sanitary Analysis, and it is capable. HydroComplete is not trying to replace a full hydraulic-modeling suite — it targets routine storm-sewer sizing, HGL checks, and defensible reporting where the friction is transcription and reviewability.

### Methods (public domain)

Rational method · Kirpich & NRCS time of concentration · IDF intensity `i = a/(t+b)^c` · Manning circular flow (full barrel + normal depth) · HEC-22 minor losses

Every engine result carries a `Steps` trace (label, value, units, formula) — the same "show your work" data behind HydroComplete's web app and sealable reports.

Learn more: [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d)

### New in v0.5.0

- **HGL profile polyline** — After `HC_HGL` labels, draw a `Polyline3d` per network on `HC-HGL-PROFILE` with Z at computed upstream/downstream HGL (Yes/No prompt, default Yes).
- **Catchment Q routing** — `HC_CAPACITY`, `HC_HGL`, and reports can route Rational peak flows through pipe topology so design *Q* varies by reach.
- **Pro activation** — `HC_ACTIVATE` with online POST to hydrocomplete.com or offline `hc_live_*` stub; `HC_LICENSE` for status; `HC_REPORT_PDF` gated on Pro.

---

## Key Features (bullet list for store UI)

- **Design capacity overload check** — Design *Q* vs *Q*<sub>full</sub> with *d*/*D* and surcharge flags; optional routed catchment *Q* per pipe; label overloaded pipes on `HC-CAPACITY`
- **Manning pipe capacity** — Full-flow *Q*, velocity, normal depth, and surcharge flags for all pipes in pipe networks
- **Normal-depth HGL** — Steady profile at design *Q* with HEC-22 junction/exit losses; plan labels on `HC-HGL`; 3D profile polyline on `HC-HGL-PROFILE`
- **Catchment Q routing** — Route Rational peak flows through pipe networks for per-reach design *Q* in capacity, HGL, and reports
- **Rational method** — Peak runoff from catchment areas with composite *C* and IDF intensity
- **Live NOAA Atlas 14 IDF** — PFDS fetch from drawing geolocation; 18 embedded city presets offline
- **Formula transparency** — Every number traceable to its equation and inputs; HTML report export (free)
- **PDF Pro export** — Sealable formula-transparent Manning + HGL PDF (`HC_REPORT_PDF`)
- **Pro activation** — Email + beta token activation with online or offline validation (`HC_ACTIVATE`, `HC_LICENSE`)
- **Drawing write-back** — MText capacity and HGL labels on `HC-CAPACITY` and `HC-HGL`; HGL profile polylines on `HC-HGL-PROFILE`
- **Auto-load bundle** — One-time install; loads on Civil 3D startup (no NETLOAD for end users)
- **Audited engine** — Shared `HydroComplete.Engine` assembly unit-tested off the CAD machine

---

## Supported Products

| Product | Series | Runtime |
|---|---|---|
| **Autodesk Civil 3D 2025** | R25.0 | .NET 8 (`net8.0-windows`) |
| **Autodesk Civil 3D 2026** | R25.1 | .NET 8 (`net8.0-windows`) |

> **Note:** The portable engine targets `netstandard2.0` so the same hydraulics core ships to Civil 3D 2023–2026 in future bundle entries. The initial App Store release is scoped to **Civil 3D 2025 and 2026** per `PackageContents.xml` `SeriesMin`/`SeriesMax`.

---

## System Requirements

| Requirement | Detail |
|---|---|
| **Operating system** | Windows 10 or Windows 11 (64-bit) |
| **Host application** | Autodesk Civil 3D 2025 or 2026 (desktop application — not `accoreconsole` or plain AutoCAD) |
| **.NET runtime** | .NET 8 (included with Civil 3D 2025/2026) |
| **Drawing content** | Civil 3D pipe network objects for `HC_PIPES` / `HC_HGL` / `HC_CAPACITY`; catchment objects for `HC_RATIONAL` with catchment-driven flows; `GEOGRAPHICLOCATION` for live Atlas 14 PFDS |
| **Disk space** | &lt; 5 MB for the application bundle |
| **Network** | Not required for core analysis (offline-capable). Live Atlas 14 PFDS and Pro license validation require internet when used. |

---

## Pricing

**TBD** — Pricing model not finalized for App Store launch.

- **Planned approach:** Freemium or tiered licensing aligned with [hydrocomplete.com](https://hydrocomplete.com) web app subscriptions.
- **Early access:** Engineers on the [Civil 3D waitlist](https://hydrocomplete.com/civil3d) may receive introductory pricing before public store listing.
- **Free tier:** Core Manning, capacity checks, normal-depth HGL (labels + profile polyline), routed catchment *Q*, HTML reports (`HC_REPORT`), and command-line access.
- **Pro tier:** PDF export (`HC_REPORT_PDF`); activate via `HC_ACTIVATE` (online validation or offline `hc_live_*` stub).

*Update this section before final Publisher submission.*

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

## Legal / Trademark Notice (include in store listing footer)

Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk, Inc. HydroComplete is an independent product and is not affiliated with or endorsed by Autodesk.