# HydroComplete for Civil 3D — User Guide

Run stormwater hydrology and hydraulics **straight from your Civil 3D drawing**,
on public-domain methods, with every formula shown. This guide is task-oriented:
find the workflow you're doing and the commands that drive it. For the full
command list, `HC_ABOUT` in Civil 3D or the reference table in the
[README](../README.md).

- **Supported:** Civil 3D 2024, 2025, 2026 (Windows 64-bit).
- **Tiers:** Free (core hydraulics + HTML reports) and Pro (sealable PDF; more over
  time). See [Licensing](#licensing).

---

## 1. Install & first run

Auto-load is a one-time install; after that the plugin loads on every Civil 3D
start — no `NETLOAD`.

1. **Quit Civil 3D completely** (no `acad.exe` in Task Manager).
2. From the install folder, in PowerShell: `powershell -File .\install.ps1`
3. Launch **Civil 3D 2024/2025/2026** from the Start menu (the full desktop app).
4. The command line shows: `HydroComplete for Civil 3D <version> loaded. Type HC_ABOUT for commands.`
5. Verify any time: `powershell -File .\verify-install.ps1`

The **HydroComplete › Analysis** ribbon tab exposes every command in seven panels
(Network, Hydrology, Stormwater, Hydraulics, Analysis, Model Builder, Tools). You
can type any `HC_` command at the command line instead.

> If commands are unknown after restart, the bundle must be at
> `%APPDATA%\Autodesk\ApplicationPlugins\HydroComplete.bundle\`. Re-run
> `install.ps1` with Civil 3D closed. `NETLOAD` is only a dev fallback.

---

## 2. Workflows

### 2.1 Analyze an existing storm sewer network

You have a Civil 3D pipe network and want capacity, velocity, and HGL.

1. `HC_NETWORK` — per-network pipe/structure summary (sanity-check what was read).
2. `HC_PIPES` — Manning full-barrel capacity `Q_full` and velocity for every pipe.
   Optional: `HC_PIPES_WRITE` to label `Q_full`/`V_full` on layer `HC-CAPACITY`.
3. `HC_CAPACITY` — design **Q vs Q_full**, `d/D`, and surcharge flags. Overloaded
   pipes are flagged; `HC_CAPACITY_WRITE` labels them.
4. `HC_HGL` — tailwater-anchored steady **backwater HGL** from the outfall upstream,
   with optional HEC-22 junction/exit/bend minor losses. Writes HGL labels on
   `HC-HGL` and, if you accept the prompt, a profile polyline on `HC-HGL-PROFILE`.
5. `HC_VALIDATE` — design-criteria review: slope, capacity, velocity, cover, HGL
   flooding — the checks a plan reviewer runs.

**Design/sizing instead of checking:** `HC_SIZE` picks standard catalog pipe sizes
by target velocity / % full (Hydraflow-style). `HC_MULTIRP` gives per-pipe Q and
`d/D` across 2/10/25/100-yr return periods in one pass.

### 2.2 Hydrology — get design flows

1. `HC_RATIONAL` — Rational peak `Q = C·i·A` from catchment objects, with a NOAA
   **Atlas 14 IDF preset** (or custom `a/b/c`). With drawing geo-reference, the
   preset defaults to **auto** and can fetch live NOAA PFDS data (cached 30 days;
   falls back to 25 embedded city presets offline).
2. `HC_ATLAS14` — list the IDF presets and see live-fetch coverage/source.
3. `HC_TC` — time of concentration (Kirpich; NRCS velocity method).
4. Route catchment flows through the pipe network: when catchments exist,
   `HC_CAPACITY` and `HC_HGL` offer **Route catchment flows** so each catchment's
   Rational peak is assigned to the right pipes (per-pipe design Q).

### 2.3 Detention & pre/post comparison

1. `HC_PREPOST` — pre- vs post-development peak comparison via SCS unit hydrograph,
   using state multi-storm depths; optional detention routing.
2. `HC_HYDROGRAPH` / `HC_ROUTE_HYDRO` — build and route hydrographs (CSV export).
3. `HC_DETENTION` — size/evaluate a detention facility.

### 2.4 Water quality, BMPs & erosion

1. `HC_WQV` — water quality volume.
2. `HC_OPTIMIZE` — BMP treatment-train optimizer: the three lowest-cost BMP chains
   that meet your state's TSS/TN/TP targets.
3. `HC_WQ_TRAIN` / `HC_WQ_DIAGRAM` — treatment-train analysis and diagram.
4. `HC_BIORETENTION`, `HC_WETLAND`, `HC_SEDIMENT`, `HC_SEDIMENT_BASIN` — size and
   check specific practices.
5. `HC_SOIL` — live SSURGO soils from the drawing's geolocation.

### 2.5 Structures & special hydraulics

- `HC_CULVERT` — circular culvert headwater (FHWA HDS-5 inlet/outlet control).
- `HC_INLETS` — HEC-22 grate-on-grade inlet interception.
- `HC_GVF` — gradually-varied flow profile.
- `HC_PUMP`, `HC_LOSS`, `HC_PROFILE` / `HC_PROFILE_DXF` — pumps, minor losses,
  profiles.

### 2.6 Visual model builder (Civil 3D 2025/2026)

- `HC_DAG` — open the drag-and-drop node DAG editor (WebView2 panel) to assemble
  watersheds → BMPs → outlet as a connected model; `HC_DAG_LOAD` / `HC_DAG_SAVE`.
- `HC_NETWORK_DIAGRAM` — export an HTML/SVG network schematic.

### 2.7 Reports

- `HC_REPORT` — **free** formula-transparent **HTML** report (Manning + steady HGL),
  with KaTeX-rendered formula steps, written to `Documents\HydroComplete\`.
- `HC_REPORT_PDF` — **Pro** — the same report as a sealable PDF with full formula
  traces.

Every engine result carries a step-by-step trace (label, value, units, formula) —
that "show your work" data is what the reports render, so a reviewer can follow
every number back to its equation.

---

## 3. Methods & references

All methods are public-domain and cited so results are defensible in review.

| Area | Method / reference |
|---|---|
| Pipe capacity & velocity | Manning's equation; normal depth by bisection; surcharge at ~0.94D |
| Peak flow (small areas) | Rational method `Q = C·i·A`, area-weighted composite C |
| Rainfall intensity | IDF `i = a/(t+b)^c`; **NOAA Atlas 14** presets + live PFDS fetch |
| Time of concentration | Kirpich (1940); NRCS velocity method |
| Hydrograph / detention | SCS/NRCS dimensionless unit hydrograph; storage-indication routing |
| HGL | Tailwater-anchored steady backwater; **HEC-22** junction/exit/bend losses |
| Culverts | **FHWA HDS-5** inlet/outlet control |
| Inlets | **HEC-22** grate-on-grade interception |
| Erosion / sediment | RUSLE soil loss; sediment yield |
| Water quality | WQV; BMP removal efficiencies; treatment-train series reduction |

## 4. Licensing

- **Free:** `HC_PIPES`, `HC_CAPACITY`, `HC_HGL`, `HC_RATIONAL`, `HC_REPORT` (HTML),
  `HC_SOIL`, `HC_ATLAS14`, `HC_NETWORK_DIAGRAM`, the DAG editor, and more.
- **Pro:** `HC_REPORT_PDF` (sealable PDF) today; more Pro features over time.

**Activate Pro:**
1. Get a token (`hc_live_…`) from
   [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d).
2. `HC_ACTIVATE` → enter email, then the token (or both on one line).
3. Online, it validates against `hydrocomplete.com/api/licensing/validate`. Offline
   or if the server is unreachable, a local stub accepts well-formed tokens and
   writes `%APPDATA%\HydroComplete\license.json` (1-year expiry, 30-day refresh).
4. `HC_LICENSE` shows Free/Pro status, validation mode (online / offline-stub), and
   last check.

## 5. Troubleshooting

| Symptom | Fix |
|---|---|
| Commands unknown after restart | Re-run `install.ps1` with Civil 3D fully closed; verify bundle path under `ApplicationPlugins`. |
| "No catchments found" in `HC_RATIONAL` | The drawing has no catchment objects; add them or enter area manually. |
| Reports not where expected | If Documents is redirected (OneDrive), they land under the redirected `Documents\HydroComplete\`. |
| `HC_REPORT_PDF` says Pro | Activate with `HC_ACTIVATE`, or use free `HC_REPORT` (HTML). |
| Atlas 14 shows "embedded" not "live" | No drawing geolocation, out of NOAA coverage, or offline — the nearest city preset is used. |

## 6. Support

`support@hydrocomplete.com` · [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d)

*Civil 3D and AutoCAD are trademarks of Autodesk, Inc. HydroComplete is an
independent product, not affiliated with or endorsed by Autodesk.*
