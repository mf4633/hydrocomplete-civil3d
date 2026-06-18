# Autodesk App Store — Submission Checklist

Step-by-step checklist for publishing **HydroComplete for Civil 3D** via the
[Autodesk Developer Network Publisher](https://apps.autodesk.com/en/Publisher/Home).

**Bundle source:** `dist/HydroComplete.bundle/`  
**CI:** `.\scripts\ci.ps1` (test + build + manifest check)  
**Preflight:** `.\scripts\app-store-preflight.ps1` (icon, commands, version sync, zip layout)  
**Version bump:** `.\scripts\bump-version.ps1 -Version x.y.z`  
**Release zip:** `.\scripts\release.ps1` → `dist/HydroComplete-{version}.zip` + SHA256  
**Manual build:** `./package.sh Release` (or `dotnet build` + copy per `package.sh`)

---

## 1. Pre-submission — Account & Legal

- [ ] **Autodesk Publisher account** — Register / sign in at apps.autodesk.com Publisher portal
- [ ] **Developer agreement** — Accept current Autodesk App Store terms
- [x] **Privacy policy URL** — `https://hydrocomplete.com/privacy.html` (live, matches site footer)
- [x] **Support contact** — `support@hydrocomplete.com` (monitored inbox)
- [x] **Product landing page** — `https://hydrocomplete.com/civil3d` (linked in manifest `HelpFile`)
- [x] **Trademark disclaimer** — Include Autodesk trademark notice in listing (see `LISTING.md`)

---

## 2. Bundle Structure Verification

Autodesk auto-load bundles must follow the `*.bundle` folder convention.

### Expected layout

```
HydroComplete.bundle/
├── PackageContents.xml          ← manifest (required)
└── Contents/
    ├── HydroComplete.Civil3D.dll  ← plugin entry (ModuleName)
    ├── HydroComplete.Engine.dll   ← mapped dependency
    ├── PackageIcon.png            ← 96×96 bundle icon (App Store)
    └── net48/                     ← optional: Civil 3D 2024 only (see §2b)
        ├── HydroComplete.Civil3D.dll
        └── HydroComplete.Engine.dll
```

### Supported Civil 3D versions (v1.3.0 submission)

| Product | Series | Runtime | Bundle layout |
|---|---|---|---|
| **Civil 3D 2024** | R24.3 | .NET Framework 4.8 (`net48`) | `./Contents/net48/*.dll` |
| **Civil 3D 2025** | R25.0 | .NET 8 (`net8.0-windows`) | `./Contents/*.dll` |
| **Civil 3D 2026** | R25.1 | .NET 8 (`net8.0-windows`) | `./Contents/*.dll` |

One `RuntimeRequirements` + `ComponentEntry` pair per series. Civil 3D 2024 loads
`Contents/net48/`; 2025+ load `Contents/` net8 DLLs.

### `PackageContents.xml` requirements

| Item | Status | Notes |
|---|---|---|
| `SchemaVersion="1.0"` | ✅ Present | |
| `AppVersion` matches release | ✅ `1.3.0` | Bump via `.\scripts\bump-version.ps1` on each store upload |
| `Name` / `Description` | ✅ Present | Mirror `LISTING.md` copy |
| `ProductCode` (GUID) | ✅ `{8d07b4c8-06cb-497d-832a-dcb5b095d9fa}` | Keep stable across versions |
| `CompanyDetails` (Name, Url, Email) | ✅ Present | |
| `RuntimeRequirements` OS/Platform/Series | ✅ `Win64` / `Civil3D` / **R24.3 + R25.0 + R25.1** | Three `ComponentEntry` blocks (net48 + net8) |
| `ComponentEntry` `ModuleName` path | ✅ `./Contents/HydroComplete.Civil3D.dll` | File must exist in zip |
| `LoadOnAutoCADStartup` | ✅ `True` | |
| `Commands` block (Local + Global) | ✅ **45** `HC_*` commands listed | Full list in `LISTING.md` § Command reference; validated by `app-store-preflight.ps1` |
| `AssemblyMappings` for Engine.dll | ✅ Present | Required for split assembly |
| **Bundle icon** | ✅ Present | `Icon="./Contents/PackageIcon.png"` — 96×96 PNG in `Contents/` |
| **Help file URL** | ✅ Present | `HelpFile="https://hydrocomplete.com/civil3d"` on `ApplicationPackage` |
| **Publisher certificate / signing** | ⚠️ **Not configured** | See §3 |

### 2a. Manifest gaps to fix before upload

1. ~~**Icon**~~ — ✅ Done: `PackageIcon.png` (96×96) + `Icon` attribute on `<ApplicationPackage>`.
2. ~~**HelpFile**~~ — ✅ Done: `HelpFile="https://hydrocomplete.com/civil3d"` on `<ApplicationPackage>`.
3. ~~**Version bump workflow**~~ — ✅ Done: `.\scripts\bump-version.ps1 -Version x.y.z` syncs csproj, manifest, `Plugin.cs`, `verify-install.ps1`, `LISTING.md`, and `HC_ABOUT` header.
4. ~~**Command manifest sync**~~ — ✅ Done: all 45 `HC_*` commands in R24.3, R25.0, and R25.1 `ComponentEntry` blocks (preflight-validated).
5. **Code signing** — ⚠️ Pending (§3).

### 2b. Civil 3D 2024 (R24.3 / net48)

| Prerequisite | Status |
|---|---|
| `net48` Release build (`scripts/build-net48.ps1`) | ✅ NuGet stub refs when AutoCAD 2024 not installed |
| `Contents/net48/HydroComplete.Civil3D.dll` in bundle | ✅ Staged by `release.ps1` |
| `ComponentEntry` with `SeriesMin="R24.3" SeriesMax="R24.3"` | ✅ Present |
| `ModuleName="./Contents/net48/HydroComplete.Civil3D.dll"` | ✅ Separate path from net8 DLLs |
| Interactive auto-load test on Civil 3D 2024 | ⬜ Required before App Store listing claims 2024 support |

See README § "Civil 3D 2024 compatibility" for series/runtime background.

### Verify locally before zipping

```powershell
.\scripts\app-store-preflight.ps1
powershell -File verify-install.ps1   # after install.ps1 on a test machine
```

- [ ] `app-store-preflight.ps1` exits 0 (icon, 45 commands, version `1.3.0`, zip layout)
- [ ] Civil 3D **2025 (R25.0)** and **2026 (R25.1)** each start with banner: `HydroComplete for Civil 3D 1.3.0 loaded`
- [ ] Civil 3D **2024 (R24.3)** auto-load test (when available): banner `1.3.0 loaded`, `HC_GVF` and `HC_ROUTE_HYDRO` respond
- [ ] `verify-install.ps1` reports `Series: R25.0 OK` and `Series: R25.1 OK`
- [ ] `HC_ABOUT` lists all 45 commands
- [ ] No duplicate bundle folders under `%APPDATA%\Autodesk\ApplicationPlugins\`

---

## 3. Code Signing

Autodesk App Store submissions require a **digitally signed** bundle for trust and auto-load approval.

- [ ] Obtain a **code-signing certificate** (EV or standard OV; Authenticode for Windows)
- [ ] Sign **both DLLs** before packaging:
  - `HydroComplete.Civil3D.dll`
  - `HydroComplete.Engine.dll`
- [ ] Typical tool: `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a HydroComplete.Civil3D.dll`
- [ ] Confirm signature: `signtool verify /pa HydroComplete.Civil3D.dll`
- [ ] **Do not sign** Autodesk host DLLs (`AcMgd`, `AeccDbMgd`, etc.) — they are `Private=false` references only
- [ ] Re-run `verify-install.ps1` on a clean VM after signing (SmartScreen / trust prompt behavior)

> Unsigned bundles may load via manual `NETLOAD` in dev but can fail Publisher validation or trigger security warnings for end users.

---

## 4. Screenshots (5–10 required)

Capture at **1920×1080** or Autodesk's current minimum (check Publisher portal). Use a real storm-sewer drawing with pipe networks. See `SCREENSHOTS.md` for shot-by-shot guidance; paste captions from `SCREENSHOT_CAPTIONS.md`.

| # | Caption source | Command / view |
|---|---|---|
| 1 | `SCREENSHOT_CAPTIONS.md` #1 | HydroComplete › Analysis ribbon |
| 2 | #2 | `HC_PIPES` command-line output |
| 3 | #3 | `HC_CAPACITY` overload table |
| 4 | #4 | `HC_HGL` profile + labels |
| 5 | #5 | `HC_ANALYZE` full-network summary |
| 6 | #6 | `HC_DETENTION` pond routing |
| 7 | #7 | `HC_PREPOST` peak comparison |
| 8 | #8 | `HC_REPORT` → browser |
| 9 | #9 | `HC_ATLAS14` / `HC_RATIONAL` IDF |
| 10 | #10 | `HC_ACTIVATE` + `HC_LICENSE` |

- [ ] All screenshots free of client-identifying project data (or use anonymized sample DWG)
- [ ] Captions copied from `SCREENSHOT_CAPTIONS.md`
- [ ] At least one shot shows Civil 3D chrome (ribbon + drawing) so reviewers recognize the host app
- [ ] **Do not** commit placeholder PNGs — capture steps only until Michael shoots real frames

---

## 5. Listing Metadata (Publisher portal)

Copy from `LISTING.md`:

- [ ] **Title:** HydroComplete for Civil 3D
- [ ] **Short description** (80 chars)
- [ ] **Long description** (full markdown/HTML as allowed)
- [ ] **Category:** Civil Engineering / Hydraulics / Productivity (pick best fit in portal)
- [ ] **Supported products:** Civil 3D 2025 (R25.0), Civil 3D 2026 (R25.1)
- [ ] **Version number:** 1.3.0
- [ ] **Keywords** — paste from `LISTING.md`
- [ ] **Pricing** — set when model finalized (TBD / freemium per `LISTING.md`)
- [ ] **Release notes** — v1.3.0: `HC_ROUTE_HYDRO` routed catchment hydrographs, Civil 3D 2024 net48 bundle; v1.2.0: `HC_GVF`, `HC_PUMP`, `HC_NETWORK_EDIT`, `HC_COST`, `HC_BACKGROUND`, modal `HC_PROFILE`/`HC_INLETS`; prior: full stormwater suite (`HC_ANALYZE`, `HC_DETENTION`, `HC_PREPOST`, compliance, LandXML, etc.)

---

## 6. Upload Package

### 6a. Create release zip

From the repo root (PowerShell):

```powershell
.\scripts\app-store-preflight.ps1
.\scripts\release.ps1
```

This builds Release, refreshes `dist/HydroComplete.bundle/Contents/*.dll`, and writes
`dist/HydroComplete-{version}.zip` (version from `HydroComplete.Civil3D.csproj` unless
`-Version` is passed). Record the printed **SHA256** for your release notes / audit trail.

Optional: run full CI before packaging:

```powershell
.\scripts\ci.ps1
```

### 6b. Upload

- [ ] Confirm zip root contains `PackageContents.xml` and `Contents/` (see `scripts/README.md`)
- [ ] Upload `dist/HydroComplete-1.3.0.zip` via Publisher → New/Update App → Attach bundle
- [ ] Pass automated manifest validation (fix Icon/HelpFile if rejected)
- [ ] Submit for Autodesk review

---

## 7. Post-approval

- [ ] Update [hydrocomplete.com/civil3d](https://hydrocomplete.com/civil3d) with App Store download link
- [ ] Email waitlist with store URL
- [ ] Monitor `support@hydrocomplete.com` for install issues
- [ ] Track validation gaps in `README.md` User validation table

---

## Quick Reference

| Resource | URL / path |
|---|---|
| Privacy policy | https://hydrocomplete.com/privacy.html |
| Support email | support@hydrocomplete.com |
| Product page | https://hydrocomplete.com/civil3d |
| Bundle manifest | `dist/HydroComplete.bundle/PackageContents.xml` |
| CI script | `scripts/ci.ps1` |
| Preflight script | `scripts/app-store-preflight.ps1` |
| Version bump | `scripts/bump-version.ps1` |
| Release zip script | `scripts/release.ps1` |
| Screenshot guide | `dist/app-store/SCREENSHOTS.md` |
| Screenshot captions | `dist/app-store/SCREENSHOT_CAPTIONS.md` |
| Listing copy | `dist/app-store/LISTING.md` |