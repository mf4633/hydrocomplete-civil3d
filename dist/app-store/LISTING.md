# Autodesk App Store — Listing Copy

**Product:** HydroComplete for Civil 3D  
**Version at submission:** 0.3.0  
**Last updated:** 2026-06-17

---

## Title

**HydroComplete for Civil 3D**

---

## Short Description (≤ 80 characters)

```
Analyze storm sewers in Civil 3D — Manning, Rational, HGL, formula transparency.
```

*(79 characters)*

---

## Long Description

Stop re-typing catchment areas, pipe runs, and invert elevations into a separate hydraulics model. **HydroComplete for Civil 3D** reads your pipe networks and catchments straight from the open drawing, runs stormwater hydrology and hydraulics on audited public-domain methods, and writes results back into the model — with every formula shown line by line.

Built by a practicing PE for the everyday sizing and reporting work Civil 3D engineers do constantly. No black boxes. No manual transcription.

### What it does

- **Manning capacity** — Full-barrel flow, normal depth, peak velocity, and surcharge flags for every pipe in every pipe network (`HC_PIPES`, `HC_PIPES_WRITE`).
- **Rational peak flows** — `Q = C·i·A` from catchment geometry with composite runoff coefficients (`HC_RATIONAL`).
- **Hydraulic grade line** — Steady HGL profile with optional HEC-22 junction and exit minor losses; labels on layer `HC-HGL` (`HC_HGL`).
- **NOAA Atlas 14 IDF** — 18 embedded US city intensity–duration–frequency presets; use by name instead of hand-entering *a*, *b*, *c* (`HC_ATLAS14`, `HC_RATIONAL`).
- **Formula-transparent reports** — HTML export with step-by-step calculation traces to `Documents\HydroComplete\` (`HC_REPORT`).
- **Ribbon integration** — **HydroComplete › Analysis** tab exposes the same commands as the command line.

### Why HydroComplete

| | Typical separate-tool workflow | HydroComplete connector |
|---|---|---|
| Reads geometry from the drawing | Export / re-key | Direct from Civil 3D objects |
| Shows the equation behind every number | No | Yes — line by line |
| Results back in the drawing | Manual annotation | MText labels on dedicated layers |
| IDF curves | Manual lookup | Embedded Atlas 14 presets |

Civil 3D ships Storm and Sanitary Analysis, and it is capable. HydroComplete is not trying to replace a full hydraulic-modeling suite — it targets routine storm-sewer sizing, HGL checks, and defensible reporting where the friction is transcription and reviewability.

### Methods (public domain)

Rational method · Kirpich & NRCS time of concentration · IDF intensity `i = a/(t+b)^c` · Manning circular flow · HEC-22 minor losses

Every engine result carries a `Steps` trace (label, value, units, formula) — the same "show your work" data behind HydroComplete's web app and sealable reports.

Learn more: [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d)

---

## Key Features (bullet list for store UI)

- **Manning pipe capacity** — Full-flow *Q*, velocity, normal depth, and surcharge flags for all pipes in pipe networks
- **Rational method** — Peak runoff from catchment areas with composite *C* and IDF intensity
- **Hydraulic grade line (HGL)** — Steady profile with HEC-22 junction/exit losses; plan labels on `HC-HGL`
- **NOAA Atlas 14 IDF** — 18 embedded city presets (10-year); no manual *a*/*b*/*c* entry
- **Formula transparency** — Every number traceable to its equation and inputs; HTML report export
- **Drawing write-back** — MText capacity labels on `HC-CAPACITY` at pipe midpoints
- **Auto-load bundle** — One-time install; loads on Civil 3D startup (no NETLOAD for end users)
- **Audited engine** — Shared `HydroComplete.Engine` assembly unit-tested off the CAD machine

---

## Supported Products

| Product | Series | Runtime |
|---|---|---|
| **Autodesk Civil 3D 2026** | R25.1 | .NET 8 (`net8.0-windows`) |

> **Note:** The portable engine targets `netstandard2.0` so the same hydraulics core can ship to Civil 3D 2023–2026 in future bundle entries. The initial App Store release is scoped to **Civil 3D 2026 only** per `PackageContents.xml` `SeriesMin`/`SeriesMax`.

---

## System Requirements

| Requirement | Detail |
|---|---|
| **Operating system** | Windows 10 or Windows 11 (64-bit) |
| **Host application** | Autodesk Civil 3D 2026 (desktop application — not `accoreconsole` or plain AutoCAD) |
| **.NET runtime** | .NET 8 (included with Civil 3D 2026) |
| **Drawing content** | Civil 3D pipe network objects for `HC_PIPES` / `HC_HGL`; catchment objects for `HC_RATIONAL` with catchment-driven flows |
| **Disk space** | &lt; 5 MB for the application bundle |
| **Network** | Not required for core analysis (offline-capable). Future Pro/auth features may require internet. |

---

## Pricing

**TBD** — Pricing model not finalized for App Store launch.

- **Planned approach:** Freemium or tiered licensing aligned with [hydrocomplete.com](https://hydrocomplete.com) web app subscriptions.
- **Early access:** Engineers on the [Civil 3D waitlist](https://hydrocomplete.com/civil3d) may receive introductory pricing before public store listing.
- **Free tier (under consideration):** Core Manning + command-line access; Pro features (full HGL, Atlas 14 presets, HTML/PDF reports, account sync) gated behind hydrocomplete.com login.

*Update this section before final Publisher submission.*

---

## Keywords (search / SEO)

```
stormwater, Civil 3D, hydraulics, hydrology, Manning, Rational method, pipe network,
storm sewer, HGL, hydraulic grade line, HEC-22, IDF, NOAA Atlas 14, intensity duration frequency,
pipe capacity, runoff, catchment, time of concentration, Kirpich, NRCS, PE report,
formula transparency, storm drain, sanitary sewer, AutoCAD plugin, Civil 3D add-in
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