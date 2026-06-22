# HydroComplete for Civil 3D

A Civil 3D add-in that runs stormwater hydrology/hydraulics **straight from the
drawing** — read pipe networks and catchments, compute on public-domain methods,
and show every formula. This is the desktop companion behind
[hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d).

Status: **v1.7.0** — DAG editor panel (`HC_DAG`), KaTeX HTML reports, live SSURGO (`HC_SOIL`), network diagram export (`HC_NETWORK_DIAGRAM`), Free/Pro licensing (`HC_ACTIVATE` / `HC_LICENSE`). **Commercial (paid) add-in** — see [`docs/COMMERCIAL.md`](docs/COMMERCIAL.md). User validation below.

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
| Engine unit tests | **validated** | `dotnet test` — 405 pass, 1 skip (2026-06-19) |
| `HC_NETWORK_DIAGRAM` (HTML/SVG export) | **validated** | v1.4.0 — automated COM smoke on Pipe Networks-3 (2026-06-19); SVG + legend in OneDrive `Documents\HydroComplete\` |
| `HC_SOIL` live SSURGO | *partial* | v1.4.0 — Cecil name lookup validated in C3D; live USDA fetch engine-tested (`SsugroFetcherTests`); drawing-geo path pending on project DWG |
| `HC_REPORT` KaTeX HTML | **validated** | v1.4.0 — user confirmed report renders in browser (2026-06-19); `hc-formula-panel` + KaTeX CDN |
| `HC_ACTIVATE` online (production API) | *partial* | v1.4.0 — Fly API accepts `hc_live_beta_tester01` (`validation-preflight`); in-C3D `HC_ACTIVATE` flow pending |
| `HC_HGL` tailwater backwater (engine) | **validated** | `Hgl.SteadyBackwaterFromOutfall` anchors at outfall tailwater, steps upstream (`HglBackwaterTests`) |
| `HC_HGL` (labels + profile in Civil 3D) | *pending re-test* | v0.6.0 — tailwater prompt at outfall; labels on `HC-HGL`, polyline on `HC-HGL-PROFILE`; close C3D → `install.ps1` → run on `C-STORM` |
| `HC_HGL` (normal-depth + HEC-22 losses) | *pending re-test* | Superseded directionally by tailwater backwater; confirm command table + surcharge flags still match hand check |
| `HC_CAPACITY` (design Q vs Q_full) | *pending* | v0.4.0 — overload check with Q_des/Q_full, d/D, surcharge flags |
| `HC_CAPACITY_WRITE` (overload labels) | *pending* | v0.4.0 — MText on `HC-CAPACITY`; overload-only or all-pipes mode |
| `HC_REPORT` (HTML export) | **validated** | v1.4.0 — Pipe Networks-3 tutorial; outputs to OneDrive `Documents\HydroComplete\` (not plain `%USERPROFILE%\Documents` when folder is redirected) |
| `HC_REPORT_PDF` (Pro) | *pending* | v0.4.0 — formula-transparent PDF; requires Pro license or `HYDROCOMPLETE_PRO=1` |
| `HC_ATLAS14` (IDF preset list) | *pending* | v0.6.0 — 25 embedded presets incl. NOAA Atlas 14 Volume 12 (ID/MT); `Atlas14CoverageTests` engine-validated |
| `HC_RATIONAL` + Atlas 14 presets | *pending* | v0.3.0 — preset key instead of manual a/b/c |
| `HC_HGL` + Rational design Q | *pending* | v0.3.0 — optional catchment-driven Q when catchments exist |
| `HC_CAPACITY` / `HC_HGL` per-catchment Q routing | *pending* | v0.6.0 — route each catchment's Rational peak through pipe topology; per-pipe Q for capacity/HGL |
| Atlas 14 auto from drawing geo | *pending* | v0.3.1 — `GEOGRAPHICLOCATION` → nearest preset; Enter accepts `auto` |
| Atlas 14 live PFDS fetch | *pending* | v0.4.0 — NOAA HDSC CSV by lat/lon; 30-day cache; offline → embedded |

## Layout

```
src/HydroComplete.Engine/      Pure hydraulics, netstandard2.0, zero Autodesk deps
src/HydroComplete.Civil3D/     The add-in: ribbon, commands, Civil 3D readers (net8.0-windows; optional net48 for 2024)
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
The auto-load bundle targets **Civil 3D 2024 (R24.3, net48)**, **2025 (R25.0)**, and
**2026 (R25.1, net8)**. Defaults reference Civil 3D 2026 for net8; override
`Net8AcadDir` (or legacy `AcadDir`) when compiling against 2025:

```
dotnet build src/HydroComplete.Civil3D -c Release
dotnet build src/HydroComplete.Civil3D -c Release -p:Net8AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"
dotnet build src/HydroComplete.Civil3D -c Release -p:BuildNet48=true
```

`PackageContents.xml` lists one `ComponentEntry` per series (R25.0 and R25.1), each
pointing at `./Contents/*.dll` — no per-version subfolders required while both hosts
run .NET 8 and share a compatible API.

Host assemblies (`AcMgd`, `AcCoreMgd`, `AcDbMgd`, `AdWindows`, `AeccDbMgd`,
`AecBaseMgd`) are referenced with `Private=false` — they are never copied; the
plugin binds to them inside the running AutoCAD process.

### Civil 3D 2024 (R24.3) — net48 multi-target

| Product year | Internal series | Host .NET runtime | Plugin TFM | Bundle path |
|---|---|---|---|---|
| Civil 3D 2021 | R24.0 | .NET Framework 4.8 | `net48` | *(not bundled — build with `Net48AcadDir` for 2021)* |
| Civil 3D 2022 | R24.1 | .NET Framework 4.8 | `net48` | *(not bundled)* |
| Civil 3D 2023 | R24.2 | .NET Framework 4.8 | `net48` | *(not bundled)* |
| **Civil 3D 2024** | **R24.3** | **.NET Framework 4.8** | **`net48`** | **`Contents/net48/*.dll`** |
| Civil 3D 2025 | R25.0 | .NET 8 | `net8.0-windows` | `Contents/*.dll` |
| Civil 3D 2026 | R25.1 | .NET 8 | `net8.0-windows` | `Contents/*.dll` |

**Series codes are not the calendar year.** Civil 3D 2024 maps to **R24.3**
(`AutoCAD.NET` NuGet 24.3.0). The `net8.0-windows` build does **not** load in the
R24.3 host — `PackageContents.xml` ships a dedicated `ComponentEntry` with
`SeriesMin="R24.3" SeriesMax="R24.3"` pointing at `./Contents/net48/`.

**Build the net48 target** (multi-targets with net8 when `BuildNet48=true`):

```
dotnet build src/HydroComplete.Civil3D -c Release -p:BuildNet48=true
```

Or use the standalone staging script (also copies into `dist/HydroComplete.bundle/Contents/net48/`):

```
powershell -File scripts/build-net48.ps1
```

When AutoCAD 2024 is installed, local host DLLs are used (`Net48AcadDir`, default
`C:\Program Files\Autodesk\AutoCAD 2024\`). **Without a local CAD install**, the
net48 target still compiles using `AutoCAD.NET` 24.3.0 and `Civil3D2024.Base` NuGet
stubs — validate load behavior on a Civil 3D 2024 machine before release.

**PDFsharp:** net8 uses PDFsharp 6.2; net48 uses PDFsharp 1.5 (same namespaces,
`PdfFonts` helper bridges `XFontStyleEx` vs `XFontStyle`). **WPF dialogs** (profile,
inlet, network editor) run on both targets via `UseWPF` and `HydroDialogHost.ShowModalWindow`.

**CI:** `scripts/ci.ps1` builds net48 when `BUILD_NET48=true` or when AutoCAD 2024
is installed; otherwise skip with a note that `-p:BuildNet48=true` works offline.

## Loading in Civil 3D (no NETLOAD)

Auto-load is a **one-time install**, then every **Civil 3D 2024, 2025, or 2026**
startup loads the plugin automatically.

1. **Quit Civil 3D completely** (Task Manager: no `acad.exe` still running).
2. In PowerShell:
   ```
   powershell -File C:\Users\michael.flynn\dev\hydrocomplete-civil3d\install.ps1
   ```
3. **Launch Civil 3D 2025 or 2026** from the Start menu (the full desktop app — not
   `accoreconsole`, not plain AutoCAD).
4. Confirm the command line shows:
   `HydroComplete for Civil 3D 1.1.0 loaded. Type HC_ABOUT for commands.`

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
| `HC_NETWORK` | Per-network pipe/structure summary |
| `HC_PIPES` | Manning capacity + full-flow velocity for every pipe in every pipe network |
| `HC_PIPES_WRITE` | Label Qfull/Vfull on layer `HC-CAPACITY` at each pipe midpoint |
| `HC_CAPACITY` | Design Q vs Q_full with d/D and surcharge flags |
| `HC_CAPACITY_WRITE` | Label overloaded pipes on `HC-CAPACITY` |
| `HC_SIZE` | Standard catalog pipe sizing (velocity, % full) — Hydraflow-style |
| `HC_VALIDATE` | Design-criteria review: slope, capacity, velocity, cover, HGL flooding |
| `HC_HGL` | Tailwater backwater HGL; optional HEC-22, momentum junction, bend losses; labels + profile polyline |
| `HC_PREPOST` | Pre/post-development peak comparison (SCS UH); state multi-storm depths; optional detention routing |
| `HC_OPTIMIZE` | BMP treatment-train optimizer — top 3 lowest-cost chains vs state TSS/TN/TP targets |
| `HC_CULVERT` | Circular culvert headwater (FHWA HDS-5 inlet/outlet control) from drawing pipe or manual entry |
| `HC_MULTIRP` | Per-pipe Q and d/D for 2/10/25/100-yr return periods |
| `HC_INLETS` | HEC-22 grate-on-grade inlet interception check |
| `HC_REPORT` | Formula-transparent HTML Manning + steady HGL report to `Documents\HydroComplete\` (free) |
| `HC_REPORT_PDF` | Same report as PDF — **Pro** (requires license; use `HC_REPORT` for free HTML) |
| `HC_RATIONAL` | Rational peak Q from catchments + NOAA Atlas 14 IDF preset (or custom a/b/c) |
| `HC_ATLAS14` | List Atlas 14 IDF presets + live PFDS fetch info |
| `HC_ACTIVATE` | Activate Pro with email + beta token (`hc_live_*`) — online or offline stub |
| `HC_LICENSE` | Show Free/Pro status, validation mode, last check, and license file path |

### Beta activation (v0.5.0)

1. Join the beta at [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d) and receive your
   activation token (format: `hc_live_…`, at least 16 characters).
2. In Civil 3D, run **`HC_ACTIVATE`** (ribbon: **Activate Pro**).
3. Enter your email, then paste the token — or paste both on one line:
   `you@firm.com hc_live_your_token_here`
4. With internet, HydroComplete POSTs to
   `https://hydrocomplete.com/api/licensing/validate` (hc-refactored
   `server/routes/licensing.js`). If the server is unreachable or the token is not
   yet in the server registry, a **local offline stub** accepts well-formed
   `hc_live_*` tokens and writes `%APPDATA%\HydroComplete\license.json` (1-year expiry).
5. Run **`HC_LICENSE`** to confirm status, validation mode (`online` vs
   `offline-stub`), and last validated timestamp.
6. **`HC_REPORT_PDF`** unlocks after activation. HTML reports (`HC_REPORT`) stay free.

**Dev bypass (engineers only):** set environment variable `HYDROCOMPLETE_PRO=1`
before launching Civil 3D — skips license file checks.

**HGL profile polyline (v0.5.0):** After `HC_HGL` writes midpoint labels, the
command prompts **Draw HGL profile polyline** (default **Yes**). When enabled,
one `Polyline3d` per pipe network is drawn on layer `HC-HGL-PROFILE` (magenta).
Vertices use each pipe's upstream/downstream plan XY (Civil 3D `StartPoint` /
`EndPoint` mapped to flow direction) with Z set to the computed HGL at that end.
Prior polylines on `HC-HGL-PROFILE` are erased before redraw. Profile write-back
requires Civil 3D and is not unit-tested in-process.

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

1. **Write-back (v0.1 done)** — MText labels on `HC-CAPACITY` validated; HGL labels on `HC-HGL` in v0.2; HGL profile polyline on `HC-HGL-PROFILE` in v0.5.0 (pending validation).
2. **HGL backwater (v0.6 engine done, CAD pending)** — Tailwater-anchored steady backwater from outfall (`SteadyBackwaterFromOutfall`); HEC-22 junction/exit minor losses; full momentum backwater next.
3. **Report export** — HTML in v0.2; formula-transparent PDF mirroring the web app next.
4. **NOAA Atlas 14 (v0.4.0)** — Live PFDS fetch + cache; 25 embedded city presets as offline fallback.
5. **Account/auth (v0.5.0 done)** — `HC_ACTIVATE` writes `%APPDATA%\HydroComplete\license.json`; online POST to `/api/licensing/validate` with offline `hc_live_*` stub fallback; `HC_LICENSE` shows validation mode and last check; `HC_REPORT_PDF` gated on Pro. HGL profile polyline write-back on `HC-HGL-PROFILE` (Yes/No prompt, default Yes).
6. **Catchment flow routing (v0.6.0)** — When catchments exist, `HC_CAPACITY` and `HC_HGL` prompt **Route catchment flows** (default Yes); engine routes each catchment's Rational peak through pipe-network topology to assign per-pipe design Q (outlet structure, nearest structure, or name match). Uniform Q fallback when routing is declined or no catchments are present.

### v0.5.0 summary

- **HGL profile polyline** — `HC_HGL` draws a `Polyline3d` per network on layer `HC-HGL-PROFILE` at upstream/downstream HGL elevations (optional, default Yes).
- **Beta activation** — `HC_ACTIVATE` with email + `hc_live_*` token; online validation against `hydrocomplete.com/api/licensing/validate` with offline stub fallback; `HC_LICENSE` shows Free/Pro status and validation mode.
- **Pro gating** — `HC_REPORT_PDF` requires activated Pro license (or `HYDROCOMPLETE_PRO=1` dev bypass); HTML reports remain free.

Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk,
Inc. HydroComplete is an independent product, not affiliated with or endorsed by
Autodesk.
