# HydroComplete for Civil 3D

A Civil 3D add-in that runs stormwater hydrology/hydraulics **straight from the
drawing** — read pipe networks and catchments, compute on public-domain methods,
and show every formula. This is the desktop companion behind
[hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d).

Status: **v0.1 — live-tested.** The engine is implemented and unit-tested;
the plugin compiles against the real Civil 3D 2026 API, reads live pipe networks
from drawings (`HC_PIPES` verified on a 30-pipe storm network), and can write
Manning capacity back to pipe Description + XData (`HC_PIPES_WRITE`).

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
Defaults target Civil 3D 2026; override the install path for other releases:

```
dotnet build src/HydroComplete.Civil3D -c Release
dotnet build src/HydroComplete.Civil3D -c Release -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"
```

Host assemblies (`AcMgd`, `AcCoreMgd`, `AcDbMgd`, `AdWindows`, `AeccDbMgd`,
`AecBaseMgd`) are referenced with `Private=false` — they are never copied; the
plugin binds to them inside the running AutoCAD process.

## Loading in Civil 3D

**Quick (per session):** `NETLOAD` →
`src/HydroComplete.Civil3D/bin/Release/net8.0-windows/HydroComplete.Civil3D.dll`

**Auto-load (recommended):** `powershell -File install.ps1` (or `bash package.sh`
then copy `dist/HydroComplete.bundle` into
`%APPDATA%\Autodesk\ApplicationPlugins\`). Restart Civil 3D — the bundle should
load on startup. If it does not, type `HC_PIPES` once (command-invoked load) or
run `NETLOAD` on the DLL.

## Commands

| Command | Does |
|---|---|
| `HC_ABOUT` | List commands |
| `HC_PIPES` | Manning capacity + full-flow velocity for every pipe in every pipe network |
| `HC_PIPES_WRITE` | Write Qfull/Vfull to each pipe's Description + HYDROCOMPLETE XData |
| `HC_RATIONAL` | Composite Rational peak flow from catchments (prompts for site IDF a/b/c) |

The ribbon tab **HydroComplete › Analysis** exposes the same four.

## Roadmap

1. **Write-back (in progress)** — pipe Description/XData done; next: MText labels, HGL profile.
2. **HGL backwater** — HEC-22 energy/momentum pass with junction losses (engine).
3. **Report export** — formula-transparent PDF mirroring the web app.
4. **NOAA Atlas 14** — auto-fetch IDF coefficients by drawing location instead of prompting.
5. **Account/auth handoff** — gate Pro features against a hydrocomplete.com login.

Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk,
Inc. HydroComplete is an independent product, not affiliated with or endorsed by
Autodesk.
