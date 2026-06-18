# HydroComplete for Civil 3D

A Civil 3D add-in that runs stormwater hydrology/hydraulics **straight from the
drawing** — read pipe networks and catchments, compute on public-domain methods,
and show every formula. This is the desktop companion behind
[hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d).

Status: **v0.4.0** — see [User validation](#user-validation) below.

**Autodesk App Store:** Listing copy, submission checklist, and screenshot shot list live in [`dist/app-store/`](dist/app-store/) (`LISTING.md`, `SUBMISSION_CHECKLIST.md`, `SCREENSHOTS.md`).

## User validation

Checked off = confirmed by Michael on Civil 3D 2026 with a live storm-sewer
drawing (`C-STORM`, 30 pipes, AutoCAD 2018-format DWG). Unchecked = built but
not yet re-tested after the listed fix.

| Item | Status | Notes |
|---|---|---|
| Bundle auto-load on startup | **validated** | v0.1.1 banner on fresh start, no NETLOAD (`install.ps1` + verify-install OK) |
| `HC_ABOUT` | **validated** | Command list prints after load |
| `HC_PIPES` | **validated** | 30 pipes; dia in ft (2.00 = 24″); Q/V match hand calcs; run twice, same output |
| `HC_RATIONAL` (no catchments) | **validated** | Correctly reports *No catchments found* on this drawing |
| `NETLOAD` fallback | **validated** | Works when bundle not yet loaded; cannot reload while assembly in memory |
| `HC_PIPES_WRITE` (Description/XData) | **failed → removed** | All 30 pipes: `eBadDxfSequence` — Civil 3D parts reject arbitrary Description/XData |
| `HC_PIPES_WRITE` (MText labels, v0.1.1) | **validated** | 30 labels on layer `HC-CAPACITY` at pipe midpoints (auto-load, no NETLOAD) |
| HydroComplete ribbon tab | *pending* | Not explicitly confirmed in session |
| `HC_RATIONAL` (with catchments) | *pending* | No catchment objects in test drawing |
| Waitlist page `hydrocomplete.com/civil3d` | *deploy only* | HTTP 200 deployed; signup flow not user-tested |
| Engine unit tests | **validated** | `dotnet test` on dev machine (grows with each release) |
| `HC_HGL` (normal-depth profile + HC-HGL labels) | *pending* | v0.4.0 — Manning normal depth at design Q; v0.3.0 adds HEC-22 junction/exit losses |
| `HC_CAPACITY` (design Q vs Q_full) | *pending* | v0.4.0 — overload check with Q_des/Q_full, d/D, surcharge flags |
| `HC_CAPACITY_WRITE` (overload labels) | *pending* | v0.4.0 — MText on `HC-CAPACITY`; overload-only or all-pipes mode |
| `HC_REPORT` (HTML export) | *pending* | v0.3.0 — Manning capacity + normal-depth HGL per network (Q prompt, HEC-22 optional); `%USERPROFILE%\Documents\HydroComplete\` |
| `HC_REPORT_PDF` (Pro) | *pending* | v0.4.0 — formula-transparent PDF; requires Pro license or `HYDROCOMPLETE_PRO=1` |
| `HC_ATLAS14` (IDF preset list) | *pending* | v0.3.0 — 18 embedded NOAA Atlas 14 city curves |
| `HC_RATIONAL` + Atlas 14 presets | *pending* | v0.3.0 — preset key instead of manual a/b/c |
| `HC_HGL` + Rational design Q | *pending* | v0.3.0 — optional catchment-driven Q when catchments exist |
| Atlas 14 auto from drawing geo | *pending* | v0.3.1 — `GEOGRAPHICLOCATION` → nearest preset; Enter accepts `auto` |
| Atlas 14 live PFDS fetch | *pending* | v0.4.0 — NOAA HDSC CSV by lat/lon; 30-day cache; offline → embedded |

## Layout

```
src/HydroComplete.Engine/      Pure hydraulics, netstandard2.0, zero Autodesk deps
src/HydroComplete.Civil3D/     The add-in: ribbon, commands, Civil 3D readers (net8.0-windows)
tests/HydroComplete.Engine.Tests/  xUnit tests for the engine (run anywhere)
dist/HydroComplete.bundle/     Auto-load bundle manifest (App Store packaging format)
package.sh                     Build + assemble the bundle into dist/
```

### Why two assemblies
- **`HydroComplete.Engine`** targets `netstandard2.0` on purpose, so the *same*
  compiled engine loads into every Civil 3D runtime — 2023/.NET Framework 4.8,
  2024/.NET 7, 2025–2026/.NET 8 — with no rebuild. It has no CAD dependency and
  is fully unit-tested off a CAD machine.
- **`HydroComplete.Civil3D`** is the thin host layer: ribbon UI, `HC_*` commands,
  and the readers that pull geometry out of the drawing.

## Engine (implemented + tested)

| Method | Notes |
|---|---|
| Manning circular flow | full-barrel capacity, normal depth (bisection), peak (~0.94 D), surcharge flag |
| Rational `Q = C·i·A` | single area and area-weighted composite C |
| Time of concentration | Kirpich (1940); NRCS velocity-method reach summation |
| IDF intensity | `i = a/(t+b)^c` with a minimum-duration floor |

Every engine result carries a `Steps` trace (`CalcStep`: label, value, units,
formula) — the "show your work" data the reporting layer will render.

```
dotnet test          # 14/14 passing
```

## Building the plugin

Requires the **.NET 8 SDK** and an installed **Civil 3D** (for the host API DLLs).
The auto-load bundle targets **Civil 3D 2025 (R25.0)** and **2026 (R25.1)** with the
same `net8.0-windows` build output. Defaults reference Civil 3D 2026; override
`AcadDir` when compiling against 2025:

```
dotnet build src/HydroComplete.Civil3D -c Release
dotnet build src/HydroComplete.Civil3D -c Release -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"
```

`PackageContents.xml` lists one `ComponentEntry` per series (R25.0 and R25.1), each
pointing at `./Contents/*.dll` — no per-version subfolders required while both hosts
run .NET 8 and share a compatible API.

Host assemblies (`AcMgd`, `AcCoreMgd`, `AcDbMgd`, `AdWindows`, `AeccDbMgd`,
`AecBaseMgd`) are referenced with `Private=false` — they are never copied; the
plugin binds to them inside the running AutoCAD process.

## Loading in Civil 3D (no NETLOAD)

Auto-load is a **one-time install**, then every **Civil 3D 2025 or 2026** startup
loads the plugin automatically.

1. **Quit Civil 3D completely** (Task Manager: no `acad.exe` still running).
2. In PowerShell:
   ```
   powershell -File C:\Users\michael.flynn\dev\hydrocomplete-civil3d\install.ps1
   ```
3. **Launch Civil 3D 2025 or 2026** from the Start menu (the full desktop app — not
   `accoreconsole`, not plain AutoCAD).
4. Confirm the command line shows:
   `HydroComplete for Civil 3D 0.4.0 loaded. Type HC_ABOUT for commands.`

Check install any time:
```
powershell -File C:\Users\michael.flynn\dev\hydrocomplete-civil3d\verify-install.ps1
```

**If commands are still unknown after restart:** the bundle folder must be exactly
`%APPDATA%\Autodesk\ApplicationPlugins\HydroComplete.bundle\` with
`PackageContents.xml` + `Contents\*.dll`. Re-run `install.ps1` with Civil 3D
closed.

**NETLOAD** is only a dev fallback while Civil 3D is open and locking the DLL, or
before the one-time install above.

## Commands

| Command | Does |
|---|---|
| `HC_ABOUT` | List commands |
| `HC_PIPES` | Manning capacity + full-flow velocity for every pipe in every pipe network |
| `HC_PIPES_WRITE` | Label Qfull/Vfull on layer `HC-CAPACITY` at each pipe midpoint |
| `HC_HGL` | Steady HGL at design Q with optional HEC-22 junction/exit losses; labels on `HC-HGL` |
| `HC_REPORT` | Formula-transparent HTML Manning + steady HGL report to `Documents\HydroComplete\` (free) |
| `HC_REPORT_PDF` | Same report as PDF — **Pro** (requires license; use `HC_REPORT` for free HTML) |
| `HC_RATIONAL` | Rational peak Q from catchments + NOAA Atlas 14 IDF preset (or custom a/b/c) |
| `HC_ATLAS14` | List Atlas 14 IDF presets + live PFDS fetch info |
| `HC_LICENSE` | Show Free/Pro status, license file path, and activation link |

**Atlas 14 geolocation (v0.3.1):** When the drawing has geo-reference data
(`GEOGRAPHICLOCATION` / `Database.GeoDataObject`), `HC_RATIONAL` and the
Rational Q path in `HC_HGL` default the preset prompt to **auto**.

**Atlas 14 live fetch (v0.4.0):** With geolocation, **auto** tries NOAA HDSC PFDS
first:

```
https://hdsc.nws.noaa.gov/cgi-bin/hdsc/new/fe_text.csv?lat={lat}&lon={lon}&data=intensity&units=english&series=pds
```

The engine parses the 10-yr intensity-duration table, fits `i = a/(t+b)^c`, and
caches JSON under `%APPDATA%\HydroComplete\idf-cache\` (30-day TTL). Offline,
out-of-coverage, or fetch errors fall back to the nearest embedded city preset.
The prompt shows **live**, **cached live**, or **embedded** source. Drawings
without geo still default to `charlotte-nc`. Geolocation reading is not
unit-tested in-process (requires Civil 3D); parser/fit/cache are tested with a
recorded Charlotte fixture in `tests/HydroComplete.Engine.Tests/Fixtures/`.

The ribbon tab **HydroComplete › Analysis** exposes the same commands.

## Roadmap

1. **Write-back (v0.1 done)** — MText labels on `HC-CAPACITY` validated; HGL labels on `HC-HGL` in v0.2 (pending validation).
2. **HGL backwater (v0.3 partial)** — HEC-22 junction/exit minor losses in steady profile; full momentum backwater next.
3. **Report export** — HTML in v0.2; formula-transparent PDF mirroring the web app next.
4. **NOAA Atlas 14 (v0.4.0)** — Live PFDS fetch + cache; 18 embedded city presets as offline fallback.
5. **Account/auth handoff (skeleton)** — `LicenseGate` checks `%APPDATA%\HydroComplete\license.json` (stub: non-expired `expires` field) or dev bypass `HYDROCOMPLETE_PRO=1`; `HC_REPORT_PDF` is the first gated Pro feature; `HC_LICENSE` shows status. Online validation against hydrocomplete.com API is TODO.

Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk,
Inc. HydroComplete is an independent product, not affiliated with or endorsed by
Autodesk.
