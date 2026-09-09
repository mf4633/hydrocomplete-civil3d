# What is actually in review at Autodesk

Read this before editing any other file in this folder.

On 2026-09-09 the Publisher Corner showed two apps, both **Pending review**,
both last modified **2026-08-25**:

| App | OS | Status | App ID |
|---|---|---|---|
| HydroComplete for Civil 3D | Win32 and 64 | Pending review | 6481677613669567077 |
| Survey & Parcel Field Kit for Civil 3D | Win64 | Pending review | — |

Price in draft: **Free**. Reviews: none. No reviewer feedback of any kind.

Portal: <https://apps.autodesk.com/en/MyUploads> → Apps → Unpublished.

## The copy in review is not the copy in this folder

The text submitted to the portal was written in the portal and is tighter than
`LISTING.md`. Anyone editing `LISTING.md` or `SUBMISSION-FORM.md` and assuming
it reflects what Autodesk is reading would be wrong. The submitted description
is transcribed verbatim below so the two can be compared.

Version shown in the listing: **1.7.4, 8/25/2026**.

The repository has since moved to 1.8.0, which adds `HC_STM_IMPORT` and the
Hydraflow comparison. **None of that is in review.** It is the next update, to
be submitted after this one clears.

## Do not edit the pending submission without deciding this first

Editing a submission that is already queued is very likely to re-queue it. As
of 2026-09-09 this has been waiting 15 calendar days, which is roughly 11
business days and right at the edge of a normal Autodesk review. Changing it
now trades a known position in the queue for an unproven improvement.

The one thing worth changing, if it is ever reopened: the description's "WHAT
IT IS NOT" section answers Storm and Sanitary Analysis but never mentions
**Hydraflow Storm Sewers**, which ships with Civil 3D, is not retired, and
does storm sewer sizing and HGL. That is the closest functional overlap and the
guidelines reject a product offering "functionality that is already available
in other products of Autodesk." The 1.8.0 copy in `LISTING.md` answers it.

If this submission is rejected on those grounds, the rebuttal is already
written and the resubmission should be 1.8.0.

---

## Submitted description, verbatim (1.7.4)

HydroComplete reads your pipe networks and catchments straight out of the open drawing, runs the hydrology and hydraulics on published public-domain methods, and writes results back onto dedicated layers - with the equation behind every number shown line by line.

Built by a practising professional engineer for the routine sizing, checking and reporting work Civil 3D users do constantly - no re-keying geometry into a separate model. 52 commands on a HydroComplete ribbon tab, all available from the command line.

STORM SEWER HYDRAULICS

HC_PIPES - Manning full-barrel capacity, normal depth and velocity for every pipe

HC_CAPACITY - design Q vs full capacity, d/D and surcharge flags, optional labels

HC_HGL - steady HGL at design Q with HEC-22 junction/exit losses, plan labels and a 3D profile polyline

HC_PROFILE - chainage profile of invert, crown and HGL

HC_INLETS - HEC-22 grate-on-grade, sag and curb-opening inlet capacity

HC_CULVERT - inlet and outlet control check

HC_GVF - gradually varied flow profile, standard step

HC_PUMP - pump duty point against the system head curve

HC_SIZE - diameter and slope sizing; HC_VALIDATE - slope/capacity/velocity/cover review

HC_NETWORK - topology and connectivity audit

HYDROLOGY

HC_RATIONAL - Q = CiA from catchment geometry with composite runoff coefficients

HC_SCS - SCS/NRCS curve number runoff; HC_TC - TR-55 segmented time of concentration

HC_UNIT_HYDRO, HC_HYDROGRAPH, HC_ROUTE_HYDRO - unit hydrographs and network routing

HC_ATLAS14 - live NOAA Atlas 14 IDF by drawing geolocation (cached); embedded city presets offline

HC_LOSS - Green-Ampt, Horton or SCS CN incremental loss on a design storm

HC_CONTINUOUS - multi-year daily continuous simulation

HC_DETENTION - Modified Puls routing with multi-stage outlets

HC_PREPOST - pre vs post development; HC_MULTIRP - multi-return-period

WATER QUALITY, BMPs AND SEDIMENT

HC_WQV, HC_BMP_SIZE, HC_WQ_TRAIN - water quality volume, BMP sizing, treatment trains

HC_BIORETENTION, HC_WETLAND - practice-specific sizing

HC_SEDIMENT, HC_SEDIMENT_BASIN - RUSLE/MUSLE loads and basin sizing

HC_OPTIMIZE - least-cost BMP selection; HC_REVIEW - state/local criteria check

VISUAL MODEL BUILDER

HC_DAG - drag-and-drop node editor wiring hydrology, hydraulics and water quality into one pipeline: 20 node types, templates, charts, HTML/SVG export; models saved beside the DWG (HC_DAG_SAVE / HC_DAG_LOAD). Civil 3D 2025 and 2026 only.

REPORTS AND DATA EXCHANGE

HC_REPORT - HTML report with KaTeX-typeset equations and step-by-step traces

HC_REPORT_PDF - sealable PDF export (Pro)

HC_NETWORK_DIAGRAM, HC_WQ_DIAGRAM - HTML/SVG schematics for submittal packages

HC_LANDXML, HC_LANDXML_IMPORT - LandXML 1.2 export/import incl. box and arch shapes

HC_PROFILE_DXF, HC_COST, HC_SOIL (live USDA SSURGO), HC_BACKGROUND

METHODS

Rational | SCS/NRCS CN and unit hydrographs | TR-55 and Kirpich Tc | IDF i = a/(t+b)^c | Manning circular/box/arch, full and partial flow | HEC-22 minor losses and inlets | Modified Puls | Muskingum-Cunge | standard-step GVF | RUSLE/MUSLE. Every result carries a step trace of label, value, units and formula.

FREE AND PRO

Everything above runs in the free tier. Pro adds sealable PDF report export, activated with HC_ACTIVATE using a token from hydrocomplete.com; HC_LICENSE shows the tier. The plugin works offline - internet is used only for live Atlas 14 and SSURGO lookups and for licence validation.

WHAT IT IS NOT

Not a replacement for a full hydraulic modelling suite. It targets routine storm sewer sizing, HGL checks and defensible reporting, where the friction is transcription and review. US customary units. Drawing-driven commands need Civil 3D pipe network and catchment objects.

More at https://hydrocomplete.com/civil3d

## Submitted version notes, verbatim (1.7.4)

v1.7.4 - engine accuracy and reliability release.
- Hydrograph superposition accuracy: unit-hydrograph renormalization, adaptive timestep, mass-conserving fallback
- Full engine audit: 30 verified findings fixed with regression tests (Clark and Snyder unit hydrographs, culvert Hc/D cap, HGL key collision, arch-pipe baselines)
- HC_REPORT_PDF font resolver fix; HC_WQ_DIAGRAM crash fix; LandXML import default corrected
- Expanded end-to-end coverage: catchment-path sweep and desktop GUI smoke tests
Earlier in 1.7.x: HC_DAG visual model builder (Civil 3D 2025/2026), HC_LOSS incremental losses, HC_CONTINUOUS simulation, HC_WQ_DIAGRAM; 52 commands across Civil 3D 2024, 2025 and 2026.
