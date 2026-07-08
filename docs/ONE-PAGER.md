# HydroComplete for Civil 3D — One-Pager

*Stormwater hydrology & hydraulics, run straight from the Civil 3D drawing, on
public-domain methods, with every formula shown.*

## The problem

Civil 3D is where site and drainage design lives, but stormwater *analysis* still
leaves the model. Engineers export to separate tools — Autodesk Storm and Sanitary
Analysis, Hydraflow, Bentley StormCAD/PondPack, HydroCAD — re-enter geometry, run
the numbers, and manually reconcile results back into the drawing. It's slow,
error-prone, and the analysis drifts out of sync with the design every time the
network changes.

## The solution

HydroComplete is a Civil 3D add-in that reads the pipe network and catchments
**directly from the drawing** and computes in place: capacity, HGL, rational and
SCS hydrology, detention, water quality, BMP sizing, culverts, inlets — 52 commands
across the stormwater workflow. It writes results back as labels, profiles, and
formula-transparent reports, so the analysis stays attached to the model.

**The differentiator: transparency.** Every result carries a step-by-step trace —
label, value, units, and the exact formula — rendered into HTML/PDF reports a PE
can hand to a plan reviewer. It's not a black box; it shows its work, on cited
public-domain methods (Manning, Rational, NOAA Atlas 14, SCS/NRCS, HEC-22, FHWA
HDS-5, RUSLE).

## Why it fits Autodesk (not competes)

- **Complements Civil 3D, extends its reach.** It makes the drawing the source of
  truth for analysis — deepening Civil 3D's value in the land-development and
  municipal stormwater workflow rather than pulling work out to a separate app.
- **Native, well-behaved add-in.** Auto-load bundle, per-version `ComponentEntry`
  for Civil 3D 2024/2025/2026, a dependency-free engine that loads across runtimes,
  and a clean ribbon. Built to App Store packaging standards.
- **Fills a modern-transparency gap.** Autodesk SSA is powerful but heavyweight and
  opaque; HydroComplete targets the everyday "check this network / size this
  detention / produce a defensible report" tasks with formula-level clarity.

## Market

- **Users:** civil/site engineers and stormwater PEs at land-development,
  municipal, and consulting firms — the existing Civil 3D drainage base.
- **Adjacent SKU:** the same engine ships as an OpenCAD plugin, so the analysis
  core reaches beyond the Autodesk ecosystem while Civil 3D remains the flagship.

## Product status

- **v1.7.2**, 52 commands, three Civil 3D versions supported.
- Dependency-free engine, unit-tested; manifest App-Store-conformant.
- **Free / Pro** licensing live (server-validated with offline grace).
- Web companion at [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d)
  with the same transparent-calc approach in the browser.

## Business model

Freemium: a genuinely useful **Free** tier (core hydraulics + HTML reports) to
drive adoption, **Pro at $199/yr** (sealable PDF reports today; batch export, team
seats, and priority support next). App Store listed as Free; Pro sold direct — so
the customer relationship and margin stay in-house.

## The ask / next step

Publish on the Autodesk App Store as a free listing with direct Pro upsell, and
open a conversation about deeper Civil 3D integration and co-marketing to the
drainage user base.

---

*Contact:* support@hydrocomplete.com · hydrocomplete.com/civil3d
*Civil 3D, AutoCAD, and Storm and Sanitary Analysis are trademarks of Autodesk,
Inc. HydroComplete is an independent product, not affiliated with or endorsed by
Autodesk.*
