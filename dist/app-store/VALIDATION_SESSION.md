# Civil 3D Validation Session — HydroComplete v1.4.0

**Owner:** Michael (requires live Civil 3D)  
**Duration:** ~2 hours  
**Primary DWG:** `C-STORM` (30 pipes, no catchments)  
**Secondary DWG:** any drawing with catchments + optional detention scenario  
**Last updated:** 2026-06-19

Run this session **before** App Store submission or direct beta release. Check boxes in
`SUBMISSION_CHECKLIST.md` § Phase 1 as you complete each block.

---

## Setup (10 min)

1. **Quit Civil 3D completely** — Task Manager: no `acad.exe`.
2. Install the release bundle:
   ```powershell
   powershell -File C:\Users\michael.flynn\dev\hydrocomplete-civil3d\install.ps1
   ```
3. Verify install (optional):
   ```powershell
   powershell -File C:\Users\michael.flynn\dev\hydrocomplete-civil3d\verify-install.ps1
   ```
4. Launch **Civil 3D 2026** (full desktop app, not `accoreconsole`).
5. Confirm startup banner:
   ```
   HydroComplete for Civil 3D 1.4.0 loaded. Type HC_ABOUT for commands.
   ```
6. Open `C-STORM`. Save a copy if you will write labels (`*_VALIDATION.dwg`).

**Pass criteria:** banner shows `1.4.0`; no NETLOAD required.

---

## Block A — Ribbon & command discovery (15 min)

| Step | Action | Pass? | Notes |
|------|--------|-------|-------|
| A1 | Click **HydroComplete › Analysis** ribbon tab | ☐ | Tab visible, not grayed out |
| A2 | Click **About** button → runs `HC_ABOUT` | ☐ | |
| A3 | `HC_ABOUT` lists **46** commands | ☐ | Count in command line output |
| A4 | Spot-check ribbon buttons: Pipe Capacity, HGL Profile, HTML Report, Activate Pro | ☐ | Each invokes without error |

---

## Block B — Core hydraulics on C-STORM (20 min)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| B1 | `HC_NETWORK` | ☐ | Summary for pipe network(s); pipe/structure counts sane |
| B2 | `HC_PIPES` | ☐ | 30 pipes; dia in ft (2.00 = 24″); Q/V match hand calc on 2–3 pipes |
| B3 | `HC_PIPES_WRITE` | ☐ | 30 MText labels on layer `HC-CAPACITY` |
| B4 | `HC_CAPACITY` | ☐ | Enter design Q; table shows Q_des/Q_full, d/D, surcharge flags |
| B5 | `HC_CAPACITY_WRITE` | ☐ | Overload labels on `HC-CAPACITY` (or all-pipes mode) |
| B6 | `HC_VALIDATE` | ☐ | Design-criteria review completes without exception |

**Re-run B2 twice** — output must be identical (deterministic).

---

## Block C — HGL & profile (20 min)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| C1 | `HC_HGL` | ☐ | Tailwater prompt (default = outfall invert); accepts Enter |
| C2 | HEC-22 losses prompt | ☐ | Yes/No both work |
| C3 | Profile polyline prompt | ☐ | **Yes** → magenta `Polyline3d` on `HC-HGL-PROFILE` |
| C4 | Plan labels | ☐ | MText on layer `HC-HGL` at pipe midpoints |
| C5 | Surcharge flags | ☐ | Pipes where HGL > crown show `*` or SURCH in output |

Optional: `HC_PROFILE` modal dialog opens; `HC_PROFILE_DXF` writes a DXF to Documents.

---

## Block D — Reports (15 min)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| D1 | `HC_REPORT` | ☐ | HTML file in `%USERPROFILE%\Documents\HydroComplete\` |
| D2 | Open HTML in browser | ☐ | Manning + HGL sections; formula steps visible |
| D3 | `HC_REPORT_PDF` without license | ☐ | Pro gate message (expected) |
| D4 | `HC_ACTIVATE` | ☐ | See Block F first, then retry |
| D5 | `HC_REPORT_PDF` with Pro | ☐ | PDF opens; content matches HTML |

---

## Block E — Hydrology & Atlas 14 (20 min)

### On C-STORM (no catchments)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| E1 | `HC_RATIONAL` | ☐ | Reports *No catchments found* (correct empty state) |
| E2 | `HC_ATLAS14` | ☐ | Lists embedded presets; live-fetch info shown |

### On catchment DWG (required)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| E3 | Set `GEOGRAPHICLOCATION` (or use geo DWG) | ☐ | |
| E4 | `HC_RATIONAL` | ☐ | Peak Q per catchment; preset `auto` or named city |
| E5 | `HC_TC` | ☐ | TR-55 Tc worksheet output |
| E6 | `HC_SCS` | ☐ | CN runoff from catchments |
| E7 | `HC_CAPACITY` with routing | ☐ | **Route catchment flows = Yes** → per-pipe Q differs by tributary |

---

## Block F — Licensing (15 min)

Use a server-issued token (after licensing API deploy) or offline stub.

| Step | Action | Pass? | What to verify |
|------|--------|-------|----------------|
| F1 | `HC_LICENSE` (before activate) | ☐ | Shows Free tier |
| F2 | `HC_ACTIVATE` — paste `you@email.com hc_live_…` | ☐ | Online: *Pro activated (online)* OR offline stub message |
| F3 | `HC_LICENSE` (after activate) | ☐ | Pro; validation mode; last validated timestamp |
| F4 | `HC_REPORT_PDF` | ☐ | Unlocked |
| F5 | Revoked token test (optional) | ☐ | Server `valid:false` → activation denied, no license file |

**Test token:** `hc_live_beta_tester01` — active on production API (deployed 2026-06-19).

Paste at `HC_ACTIVATE`:
```
you@email.com hc_live_beta_tester01
```

---

## Block G — v1.3.0+ headline features (25 min)

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| G1 | `HC_GVF` | ☐ | GVF profile for trapezoidal channel; no crash |
| G2 | `HC_ROUTE_HYDRO` | ☐ | Routed hydrograph output; CSV in Documents |
| G3 | `HC_CULVERT` | ☐ | Headwater calc from pipe or manual entry |
| G4 | `HC_INLETS` | ☐ | Modal dialog opens; HEC-22 result prints |
| G5 | `HC_NETWORK_DIAGRAM` | ☐ | HTML/SVG opens in browser from Documents |
| G6 | `HC_SOIL` | ☐ | Live SSURGO or regional fallback; HSG + K-factor |
| G7 | `HC_REPORT` | ☐ | KaTeX formulas render in browser (not plain text) |

If catchment DWG available:

| Step | Command | Pass? | What to verify |
|------|---------|-------|----------------|
| G8 | `HC_ANALYZE` | ☐ | Full-network summary |
| G9 | `HC_DETENTION` | ☐ | Pond routing completes |
| G10 | `HC_PREPOST` | ☐ | Pre/post peak comparison |

---

## Block H — Civil 3D 2024 (optional, if installed)

Repeat **Setup** + **Block A** + **B2** + **G1** + **G2** on Civil 3D **2024**.

| Step | Pass? | What to verify |
|------|-------|----------------|
| H1 Auto-load banner `1.4.0` on 2024 | ☐ | Loads `Contents/net48/` DLLs |
| H2 `HC_PIPES` on C-STORM | ☐ | Same 30-pipe result as 2026 |
| H3 `HC_GVF` | ☐ | No assembly load error |

Skip Block H in the App Store listing if you cannot test 2024 before submission.

---

## Block I — Marketing site (5 min)

| Step | Action | Pass? | What to verify |
|------|--------|-------|----------------|
| I1 | Open https://hydrocomplete.com/civil3d | ☐ | HTTP 200; page renders |
| I2 | Submit waitlist form with test email | ☐ | Success message; event in analytics dashboard |

---

## After the session

1. Note failures in `README.md` User validation table (flip *pending* → **validated** or *failed*).
2. File bugs for any failures; re-run `install.ps1` after fixes.
3. If all Block A–G pass on 2026, you are **beta-ready** for direct zip/waitlist release.
4. For App Store, continue `SUBMISSION_CHECKLIST.md` Phase 2+ (signing, screenshots, Publisher).

**Agent-automated gate (no C3D):** run before and after your session:

```powershell
powershell -File scripts\validation-preflight.ps1
```