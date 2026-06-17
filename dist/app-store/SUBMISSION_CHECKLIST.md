# Autodesk App Store — Submission Checklist

Step-by-step checklist for publishing **HydroComplete for Civil 3D** via the
[Autodesk Developer Network Publisher](https://apps.autodesk.com/en/Publisher/Home).

**Bundle source:** `dist/HydroComplete.bundle/`  
**Build command:** `./package.sh Release` (or `dotnet build` + copy per `package.sh`)

---

## 1. Pre-submission — Account & Legal

- [ ] **Autodesk Publisher account** — Register / sign in at apps.autodesk.com Publisher portal
- [ ] **Developer agreement** — Accept current Autodesk App Store terms
- [ ] **Privacy policy URL** — `https://hydrocomplete.com/privacy.html` (live, matches site footer)
- [ ] **Support contact** — `support@hydrocomplete.com` (monitored inbox)
- [ ] **Product landing page** — `https://hydrocomplete.com/civil3d` (optional but recommended; linked in manifest)
- [ ] **Trademark disclaimer** — Include Autodesk trademark notice in listing (see `LISTING.md`)

---

## 2. Bundle Structure Verification

Autodesk auto-load bundles must follow the `*.bundle` folder convention.

### Expected layout

```
HydroComplete.bundle/
├── PackageContents.xml          ← manifest (required)
└── Contents/
    ├── HydroComplete.Civil3D.dll  ← plugin entry (ModuleName)
    └── HydroComplete.Engine.dll   ← mapped dependency
```

### `PackageContents.xml` requirements

| Item | Status | Notes |
|---|---|---|
| `SchemaVersion="1.0"` | ✅ Present | |
| `AppVersion` matches release | ✅ `0.3.0` | Bump on each store upload |
| `Name` / `Description` | ✅ Present | Mirror `LISTING.md` copy |
| `ProductCode` (GUID) | ✅ `{8d07b4c8-06cb-497d-832a-dcb5b095d9fa}` | Keep stable across versions |
| `CompanyDetails` (Name, Url, Email) | ✅ Present | |
| `RuntimeRequirements` OS/Platform/Series | ✅ `Win64` / `Civil3D` / `R25.1` | Single-version scope OK for v1 |
| `ComponentEntry` `ModuleName` path | ✅ `./Contents/HydroComplete.Civil3D.dll` | File must exist in zip |
| `LoadOnAutoCADStartup` | ✅ `True` | |
| `Commands` block (Local + Global) | ✅ All 7 `HC_*` commands listed | |
| `AssemblyMappings` for Engine.dll | ✅ Present | Required for split assembly |
| **Bundle icon** | ⚠️ **Missing** | Add `Icon="./Contents/PackageIcon.png"` (see §2a) |
| **Help file URL** | ⚠️ **Missing** | Add `HelpFile` on `ApplicationPackage` or `ComponentEntry` |
| **Publisher certificate / signing** | ⚠️ **Not configured** | See §3 |

### 2a. Manifest gaps to fix before upload

1. **Icon** — Autodesk listings require a product icon (typically 96×96 PNG).
   - Add `dist/HydroComplete.bundle/Contents/PackageIcon.png` (export from `/assets/images/icon-192.png` on hydrocomplete.com).
   - Add attribute: `Icon="./Contents/PackageIcon.png"` on `<ApplicationPackage>`.
2. **HelpFile** — Point to `https://hydrocomplete.com/civil3d` or a dedicated help page:
   - `HelpFile="https://hydrocomplete.com/civil3d"` on `<ApplicationPackage>`.
3. **Version bump workflow** — Sync `AppVersion` in XML, `Plugin.cs` banner, and `LISTING.md` on every submission.

### Verify locally before zipping

```powershell
powershell -File verify-install.ps1   # after install.ps1 on a test machine
```

- [ ] Civil 3D 2026 starts with banner: `HydroComplete for Civil 3D 0.3.0 loaded`
- [ ] `HC_ABOUT` lists all commands
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

## 4. Screenshots (5–6 required)

Capture at **1920×1080** or Autodesk's current minimum (check Publisher portal). Use a real storm-sewer drawing with pipe networks. See `SCREENSHOTS.md` for shot-by-shot guidance.

| # | Caption (for store) | Command / view |
|---|---|---|
| 1 | **Ribbon tab** — HydroComplete Analysis commands one click away | HydroComplete › Analysis ribbon |
| 2 | **Manning capacity** — Full-flow Q and velocity for every pipe in the network | `HC_PIPES` command-line output |
| 3 | **Plan labels** — Capacity annotations written back on layer HC-CAPACITY | `HC_PIPES_WRITE` plan view |
| 4 | **Hydraulic grade line** — Steady HGL with HEC-22 losses labeled on HC-HGL | `HC_HGL` plan + profile |
| 5 | **Formula-transparent report** — Step-by-step Manning calcs in HTML export | `HC_REPORT` → browser |
| 6 | **Atlas 14 IDF presets** — 18 embedded NOAA city curves, no manual a/b/c | `HC_ATLAS14` command output |

- [ ] All screenshots free of client-identifying project data (or use anonymized sample DWG)
- [ ] Captions written per table above
- [ ] At least one shot shows Civil 3D chrome (ribbon + drawing) so reviewers recognize the host app

---

## 5. Listing Metadata (Publisher portal)

Copy from `LISTING.md`:

- [ ] **Title:** HydroComplete for Civil 3D
- [ ] **Short description** (80 chars)
- [ ] **Long description** (full markdown/HTML as allowed)
- [ ] **Category:** Civil Engineering / Hydraulics / Productivity (pick best fit in portal)
- [ ] **Supported product:** Civil 3D 2026
- [ ] **Version number:** 0.3.0
- [ ] **Keywords** — paste from `LISTING.md`
- [ ] **Pricing** — set when model finalized (TBD / freemium per `LISTING.md`)
- [ ] **Release notes** — summarize v0.3.0: Atlas 14 presets, HEC-22 HGL losses, HTML reports

---

## 6. Upload Package

- [ ] Zip **`HydroComplete.bundle`** folder (not its parent) — the zip root must contain `PackageContents.xml`
- [ ] Upload via Publisher → New/Update App → Attach bundle
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
| Screenshot guide | `dist/app-store/SCREENSHOTS.md` |
| Listing copy | `dist/app-store/LISTING.md` |