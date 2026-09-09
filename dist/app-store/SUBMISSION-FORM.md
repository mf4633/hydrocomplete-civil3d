# Autodesk App Store — Submission Form (copy-paste ready)

Every field below holds the **final value** to paste directly into the
[Publisher portal](https://aps.autodesk.com/app-store/publisher-center) — no cross-referencing.
Source of record is `LISTING.md`; the mechanical field→source mapping and gotchas are in
`PORTAL-FIELDS.md`. Fill the two `{{placeholders}}` on submission/launch day.

**Legend:** ✅ paste as-is · ⌨️ type/select · 📎 upload a file · ⚠️ do on the day

---

## App metadata

**App name / Title** ⌨️ (must match `PackageContents.xml` `Name` exactly)
```
HydroComplete for Civil 3D
```

**Version** ⌨️ (must equal manifest `AppVersion` and csproj `<Version>`)
```
1.8.0
```

**Category** ⌨️ — select **Civil Engineering** (nearest Autodesk taxonomy label if not exact)

**Short description** ✅ (**≤ 80 chars — this is 78, do not lengthen**)
```
Civil 3D storm sewers — capacity overload, normal-depth HGL, live Atlas 14 IDF.
```

**Help / product URL** ⌨️ (matches manifest `HelpFile`)
```
https://hydrocomplete.com/civil3d
```

**Supported products** ⌨️ — Civil 3D **2024, 2025, 2026** (match manifest `RuntimeRequirements`: R24.3 / R25.0 / R25.1)

---

## Long description ✅

Portal accepts limited formatting — paste, then fix any bullet/heading rendering.

```
Stop re-typing catchment areas, pipe runs, and invert elevations into a separate hydraulics model. HydroComplete for Civil 3D reads your pipe networks and catchments straight from the open drawing, runs stormwater hydrology and hydraulics on audited public-domain methods, and writes results back into the model — with every formula shown line by line.

Built by a practicing PE for the everyday sizing and reporting work Civil 3D engineers do constantly. No black boxes. No manual transcription.

WHAT IT DOES
• Design capacity check — Compare design Q to full-barrel Manning capacity for every pipe; see Qdes/Qfull, d/D, and surcharge flags (HC_CAPACITY, HC_CAPACITY_WRITE). When catchments exist, optionally route catchment flows so each pipe gets its tributary Q.
• Manning capacity — Full-barrel flow, normal depth, peak velocity, and surcharge flags for every pipe in every pipe network (HC_PIPES, HC_PIPES_WRITE).
• Normal-depth hydraulic grade line — Steady HGL at design Q using Manning normal depth, with optional HEC-22 junction and exit losses; plan labels on layer HC-HGL; optional 3D profile polyline on HC-HGL-PROFILE (HC_HGL).
• Rational peak flows — Q = C·i·A from catchment geometry with composite runoff coefficients (HC_RATIONAL).
• Live NOAA Atlas 14 IDF — PFDS intensity-duration-frequency fetch from drawing geolocation (30-day cache); 25 embedded US city presets as offline fallback (HC_ATLAS14, HC_RATIONAL).
• Formula-transparent reports — HTML export with KaTeX-rendered equations and step-by-step calculation traces (HC_REPORT); sealable PDF export with Pro (HC_REPORT_PDF).
• Network schematic export — HC_NETWORK_DIAGRAM writes an HTML/SVG pipe-network diagram for review packages and submittals.
• Live SSURGO soils — HC_SOIL queries USDA SSURGO by drawing geolocation with regional fallback; hydrologic soil group, K-factor, and BMP suitability hints.
• Pro activation — Activate with email and beta token via online validation or offline stub; HC_LICENSE shows tier and last check (HC_ACTIVATE, HC_LICENSE).
• Hydraflow Storm Sewers import — HC_STM_IMPORT reads a Hydraflow Storm Sewers .stm project, both the standalone format and the Civil 3D extension format, reports its pipes, structures, design storm, tailwater and inlets, and writes a LandXML file that Civil 3D's own pipe network import builds a network from. Free.
• Ribbon integration — HydroComplete › Analysis tab exposes the same commands as the command line.

WHY HYDROCOMPLETE
The analysis stays attached to the model instead of drifting out of sync with the design. Geometry is read straight from Civil 3D objects (no export/re-key), every number carries the equation behind it, and results write back as MText labels on dedicated layers.

Autodesk ships two hydraulics tools alongside Civil 3D, and HydroComplete replaces neither. Storm and Sanitary Analysis is a full hydraulic-modeling suite and HydroComplete is not one. Hydraflow Storm Sewers does storm sewer design and HGL well, but it is a separate external application: it does not read or write Civil 3D pipe network objects, so its results live outside the drawing and a finished Hydraflow project cannot become a network in the model. That gap is what this add-in fills. HydroComplete runs on the drawing's own pipe networks and catchments and writes back to them, and HC_STM_IMPORT turns an existing Hydraflow project into a Civil 3D pipe network.

The engine has been independently compared line by line against Hydraflow Storm Sewers on two real projects — slopes, accumulated C·A, intensity, Rational flow, Manning capacity, which lines surcharge, which structure floods and the gradeline — with every remaining difference written down and traced to the method choice behind it, rather than smoothed over. That comparison is the publisher's own work; Autodesk has not reviewed or endorsed it.

METHODS (public domain)
Rational method · Kirpich & NRCS time of concentration · IDF intensity i = a/(t+b)^c · Manning circular/box/arch flow (full barrel + normal depth) · HEC-22 minor losses · FHWA HDS-5 culverts · SCS/NRCS runoff & unit hydrographs · Modified Puls detention · RUSLE/MUSLE. Every engine result carries a Steps trace (label, value, units, formula).

Learn more: hydrocomplete.com/civil3d
```

---

## What's New / release notes ✅ (v1.8.0)

⚠️ Trim to the portal's char limit if it rejects the full block.

```
HydroComplete for Civil 3D v1.8.0 - open your Hydraflow projects in the drawing.

NEW IN v1.8.0
- HC_STM_IMPORT reads a Hydraflow Storm Sewers .stm project, both the standalone format and the Civil 3D extension format, and writes a LandXML file that Civil 3D's own pipe network import builds a network from. It reports the pipes, structures, design storm, IDF curves, tailwater, junction losses and inlet data the file carries. Free.
- The engine is now compared line by line against Hydraflow Storm Sewers on two real projects. Slopes, accumulated C*A, intensity, Rational flow, Manning capacity, which lines surcharge, which structure floods and all eight inlet captures match the numbers Hydraflow printed; the gradeline matches within 0.15 ft. Every remaining difference is traced to the method choice behind it and written down, not smoothed over.
- HC_HGL correction: the grade line is now held at or above the water actually standing in the pipe, the crown of a surcharged reach or the normal depth of a partly-full one. Before this it stepped straight through such a structure and reported a grade line that was too LOW, which under-reports flooding. On the validation network the error was 0.36 ft carried up the whole run. THIS CHANGES REPORTED HGL ELEVATIONS on any network where a pipe runs partly full below a surcharged one; re-run any HGL check you rely on.
- HC_INLETS correction: a grate in a sag is now checked on its real HEC-22 perimeter (2L+W, curb side excluded) and capped by the orifice form once it drowns, taking the lesser of the two. The old check used the bare grate length, about a third of a square grate's capacity, and had no orifice limit at all, so it over-promised past roughly 1.4 ft of ponding. HC_INLETS now asks for the grate width; leave it blank and you get the old conservative answer unchanged.
- 53 HC_* commands across Civil 3D 2024, 2025 and 2026.

Earlier in 1.7.x: engine accuracy release with 30 audited findings fixed and regression tests; HC_DAG visual model builder; HC_LOSS incremental losses; HC_CONTINUOUS simulation; HC_WQ_DIAGRAM.
```

---

## System requirements ✅

```
Operating system: Windows 10 or Windows 11 (64-bit)
Host application: Autodesk Civil 3D 2024, 2025, or 2026 (desktop application — not accoreconsole or plain AutoCAD)
.NET runtime: .NET Framework 4.8 (Civil 3D 2024) or .NET 8 (Civil 3D 2025/2026 — included with host)
Drawing content: Civil 3D pipe network objects for HC_PIPES / HC_HGL / HC_CAPACITY; catchment objects for HC_RATIONAL; GEOGRAPHICLOCATION for live Atlas 14 PFDS
Disk space: < 5 MB for the application bundle
Network: not required for core analysis (offline-capable); live Atlas 14 PFDS and Pro validation need internet when used
```

---

## Keywords ✅ (portal may cap the count — first ~20 are most specific)

```
stormwater, Civil 3D, hydraulics, hydrology, Manning, Rational method, pipe network, storm sewer, HGL, hydraulic grade line, HEC-22, IDF, NOAA Atlas 14, intensity duration frequency, pipe capacity, surcharge, overload, normal depth, runoff, catchment, time of concentration, Kirpich, NRCS, PE report, formula transparency, storm drain, sanitary sewer, AutoCAD plugin, Civil 3D add-in, PDF report
```

---

## Publisher / legal ✅

| Field | Value |
|---|---|
| Company / publisher | `HydroComplete` |
| Support email | `support@hydrocomplete.com` (⚠️ monitor from launch day) |
| Product URL | `https://hydrocomplete.com/civil3d` |
| Privacy policy URL | `https://hydrocomplete.com/privacy.html` (must resolve HTTP 200) |
| EULA / license | desktop add-in terms — `terms.html` §12 on hydrocomplete.com |

**Trademark notice** ✅ (listing footer)
```
Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk, Inc. HydroComplete is an independent product and is not affiliated with or endorsed by Autodesk.
```

---

## Pricing ⌨️

- Business model: **Free** listing.
- In-app purchase disclosure: **Yes — external Pro upgrade** ($199/year via hydrocomplete.com/civil3d, Stripe; unlocked by `HC_ACTIVATE`). Disclose so review isn't surprised by the gated `HC_REPORT_PDF`.
- Payout/tax: not needed for a Free listing.

---

## Artifacts to upload 📎

| Item | Source | Notes |
|---|---|---|
| App bundle (zip) | `dist/HydroComplete-1.8.0.zip` | ⚠️ Built + **signed** on a Civil 3D box (`release.ps1` → `sign-release.ps1`); run `app-store-preflight.ps1 -RequireSigning` first (exit 0). |
| App icon / thumbnail | HydroComplete logo | Larger store thumbnail, distinct from the 96×96 `PackageIcon.png`. |
| Screenshots (≥3, up to ~8) | per `SCREENSHOTS.md` | 1920×1080; captions from `SCREENSHOT_CAPTIONS.md`; scrub client data. |
| Demo video (optional) | 60–90s | Link a YouTube/Vimeo URL; materially lifts conversion. |

---

## Placeholders to fill on the day ⚠️

- `{{store_link}}` — the live Autodesk App Store URL (only exists after approval; needed in `LAUNCH-COMMS.md`).
- `{{hc_live_token}}` — a **unique** `hc_live_*` beta token per waitlist recipient (never reuse one).

## Pre-submit gate (all must be true)

- [ ] `app-store-preflight.ps1 -RequireSigning` exits 0 (signed, versions in sync, 52 commands, icon present).
- [ ] Zip installs/uninstalls cleanly on a fresh Civil 3D profile.
- [ ] All 52 commands launch without an unhandled exception (`VALIDATION_SESSION.md`).
- [ ] `HC_ACTIVATE` works online **and** the offline stub works with the network off.
- [ ] The three public URLs (product, privacy, support) resolve.
- [ ] License keys provisioned as Fly secrets so activation succeeds during review.

Then hit **Submit for review** (~1–2 weeks; respond to reviewer notes within a business day).
