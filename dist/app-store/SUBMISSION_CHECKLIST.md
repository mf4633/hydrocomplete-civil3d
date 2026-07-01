# HydroComplete C3D — Release & App Store Checklist

**Target version:** 1.7.2  
**Last updated:** 2026-06-24

### Legend

| Tag | Meaning |
|-----|---------|
| 🤖 | Agent/automation — no Civil 3D required |
| 👤 | Michael — requires live Civil 3D or business action |
| ⏳ | Blocked on external dependency (cert, Autodesk review, PayPal) |

### Quick commands

```powershell
# Automated gate (no C3D)
.\scripts\validation-preflight.ps1

# Full CI
.\scripts\ci.ps1

# App Store bundle check
.\scripts\app-store-preflight.ps1

# Release zip + SHA256
.\scripts\release.ps1

# Sign DLLs (after cert obtained)
$env:HC_SIGN_CERT_THUMBPRINT = 'THUMBPRINT'
.\scripts\sign-release.ps1
.\scripts\release.ps1
```

**Civil 3D validation guide:** [`VALIDATION_SESSION.md`](VALIDATION_SESSION.md)

---

## Phase 0 — Automated gate (🤖 no C3D)

Run `.\scripts\validation-preflight.ps1` before every release candidate.

| Item | Owner | Status |
|------|-------|--------|
| Engine unit tests (`dotnet test`) — 405+ pass | 🤖 | [x] 2026-06-19 |
| CI build + manifest command sync (`ci.ps1`) | 🤖 | [x] 2026-06-19 — fixed AcadDir quoting bug |
| App Store preflight (`app-store-preflight.ps1`) exits 0 | 🤖 | [x] 2026-06-26 — 52 commands |
| Bundle DLLs present and non-trivial size | 🤖 | [x] C3D 304 KB, Engine 513 KB |
| `hydrocomplete.com/civil3d` HTTP 200 | 🤖 | [x] 2026-06-19 |
| `hydrocomplete.com/privacy.html` HTTP 200 | 🤖 | [x] 2026-06-19 |
| Licensing API accepts `hc_live_beta_tester01` | 🤖 | [x] 2026-06-19 — Fly deploy; `JWT_SECRET` fallback |
| `hydrocomplete.com/api/*` proxy to Fly | 🤖 | [x] 2026-06-23 — Netlify proxy live |
| GitHub CI — hc-refactored test suite (505 tests) | 🤖 | [x] 2026-06-23 — `.github/workflows/ci.yml` |
| Stripe plugin checkout + key issuance | 🤖 | [x] 2026-06-23 — `create-plugin-checkout` + webhook; set `STRIPE_PRICE_CIVIL3D_PRO` on Fly |
| Civil 3D landing purchase CTA | 🤖 | [x] 2026-06-23 — `civil3d.html` $199/yr Stripe card |
| Desktop add-in Terms (Section 12) | 🤖 | [x] 2026-06-23 — `terms.html` civil3d + opencad SKUs |
| Plugin `HC_ACTIVATE` online URL | 🤖 | [x] 2026-07-01 — `hydrocomplete.com/api/licensing/validate` |
| Release zip + SHA256 (`release.ps1`) | 🤖 | [x] `HydroComplete-1.4.0.zip` SHA256 `2075812B…FD0FE` (2026-06-19 rebuild) |
| Civil 3D parity smoke (`smoke-civil3d-parity.ps1`) | 🤖 | [x] 2026-06-19 — KaTeX report + network diagram on Pipe Networks-3 |
| GitHub remote pushed (`hydrocomplete-civil3d`) | 🤖 | [x] `v1.4.0` tag + master `54752d1` |

**Version 1.4.0 shipped (2026-06-19):** SSURGO live API, KaTeX HTML reports, `HC_NETWORK_DIAGRAM`, licensing URL fix.

---

## Phase 1 — Civil 3D validation (👤)

Follow [`VALIDATION_SESSION.md`](VALIDATION_SESSION.md) on Civil 3D **2026** with `C-STORM`.

### Already validated (v0.1.1 core)

- [x] Bundle auto-load — startup banner, no NETLOAD
- [x] `HC_ABOUT`, `HC_PIPES`, `HC_RATIONAL` (empty catchments), `NETLOAD` fallback
- [x] `HC_PIPES_WRITE` — MText on `HC-CAPACITY` (30/30 pipes)

### Block A — Ribbon & discovery

- [ ] HydroComplete › Analysis ribbon tab visible
- [ ] Ribbon buttons invoke commands
- [ ] `HC_ABOUT` lists **52** commands

### Block B — Core hydraulics (C-STORM)

- [ ] `HC_NETWORK`
- [ ] `HC_CAPACITY` + `HC_CAPACITY_WRITE`
- [ ] `HC_VALIDATE`

### Block C — HGL & profile

- [ ] `HC_HGL` tailwater backwater + labels on `HC-HGL`
- [ ] Profile polyline on `HC-HGL-PROFILE`
- [ ] Surcharge flags match hand check

### Block D — Reports

- [ ] `HC_REPORT` HTML opens in browser with formula steps
- [ ] `HC_REPORT_PDF` gated without license; works after `HC_ACTIVATE`

### Block E — Hydrology (needs catchment DWG)

- [ ] `HC_RATIONAL` with catchments + Atlas 14 preset
- [ ] `HC_ATLAS14` live PFDS + embedded fallback
- [ ] Catchment Q routing in `HC_CAPACITY` / `HC_HGL`

### Block F — Licensing

- [ ] `HC_ACTIVATE` online with `hc_live_*` server token
- [ ] `HC_LICENSE` shows Pro + validation mode
- [ ] Offline stub still works when server unreachable

### Block G — v1.4.0 features

- [ ] `HC_GVF`
- [ ] `HC_ROUTE_HYDRO` + CSV export
- [x] `HC_NETWORK_DIAGRAM` (HTML/SVG in Documents) — automated 2026-06-19
- [ ] `HC_SOIL` live SSURGO (drawing geo) — name lookup only so far
- [x] `HC_REPORT` KaTeX formulas in browser — user confirmed 2026-06-19
- [ ] `HC_ANALYZE` / `HC_DETENTION` / `HC_PREPOST` (catchment DWG)

### Block H — Civil 3D 2024 (optional)

- [ ] Auto-load on R24.3 (net48 bundle)
- [ ] `HC_PIPES`, `HC_GVF` on 2024

### Block I — Marketing

- [ ] Waitlist form on `hydrocomplete.com/civil3d` submits (`c3d_waitlist` analytics event)

---

## Phase 2 — Business & licensing (👤 + 🤖)

| Item | Owner | Status |
|------|-------|--------|
| Pricing model decided (free / paid / freemium) | 👤 | [x] 2026-07-01 — Freemium: Free + Pro $199/yr (`LISTING.md`, `civil3d.html` Stripe) |
| PayPal publisher account (if paid on Marketplace) | 👤 ⏳ | [ ] |
| `hc_live_*` tokens in production licensing API | 🤖 | [x] code ready in `hc-refactored/server/routes/licensing.js` |
| Deploy licensing API to Fly.io | 👤 | [ ] `flyctl deploy --app hc-refactored` |
| Stripe → token issuance for paying customers | 👤 | [ ] future — manual `HC_LICENSE_KEYS` env OK for beta |
| Beta token distribution to waitlist | 👤 | [ ] |

**Env vars for production (Fly.io):**

```
LICENSE_SECRET=<existing>
HC_LICENSE_KEYS=hc_live_waitlist_user1,hc_live_waitlist_user2
# or full JSON:
HC_LICENSE_STORE_JSON={"hc_live_firm_x":{"email":"pe@firm.com","expires":"2027-12-31","features":["reports","export","civil3d"]}}
```

---

## Phase 3 — Code signing (👤 ⏳)

| Item | Owner | Status |
|------|-------|--------|
| Obtain Authenticode code-signing certificate (OV/EV) | 👤 ⏳ | [ ] |
| Sign net8 DLLs + net48 DLLs (`scripts/sign-release.ps1`) | 👤 | [ ] scaffold ready |
| Verify signatures (`signtool verify /pa`) | 👤 | [ ] |
| Test signed bundle on clean VM (SmartScreen) | 👤 | [ ] |

---

## Phase 4 — Screenshots & video (👤)

See `SCREENSHOTS.md` + `SCREENSHOT_CAPTIONS.md`. Capture at **1920×1080**.

| # | Shot | Status |
|---|------|--------|
| 1 | Ribbon tab | [ ] |
| 2 | `HC_PIPES` output | [ ] |
| 3 | `HC_CAPACITY` overload | [ ] |
| 4 | `HC_HGL` profile + labels | [ ] |
| 5 | `HC_ANALYZE` summary | [ ] |
| 6 | `HC_DETENTION` | [ ] |
| 7 | `HC_PREPOST` | [ ] |
| 8 | `HC_REPORT` in browser | [ ] |
| 9 | `HC_ATLAS14` / `HC_RATIONAL` | [ ] |
| 10 | `HC_ACTIVATE` + `HC_LICENSE` | [ ] |
| — | 60–90s demo video (recommended) | [ ] |

- [ ] No client-identifying data in shots
- [ ] Do not commit placeholder PNGs to repo

---

## Phase 5 — Autodesk Publisher (👤 ⏳)

### Account & legal

- [ ] Autodesk Publisher account at [apps.autodesk.com](https://apps.autodesk.com/en/Publisher/Home)
- [ ] Developer agreement accepted
- [x] Privacy policy — `https://hydrocomplete.com/privacy.html`
- [x] Support — `support@hydrocomplete.com`
- [x] Product page — `https://hydrocomplete.com/civil3d` (manifest `HelpFile`)
- [x] Trademark disclaimer in listing (`LISTING.md`)

### Bundle structure (🤖 verified)

- [x] `PackageContents.xml` — SchemaVersion 1.0, AppVersion, ProductCode GUID stable
- [x] R24.3 + R25.0 + R25.1 `ComponentEntry` blocks
- [x] 46 `HC_*` commands in manifest
- [x] `PackageIcon.png` (96×96)
- [x] `AssemblyMappings` for Engine.dll
- [ ] Code signing (Phase 3)

### Listing metadata (copy from `LISTING.md`)

- [ ] Title, short + long description
- [x] Category — Civil Engineering (`LISTING.md`)
- [ ] Keywords, release notes
- [ ] Supported products: 2024 (if Block H pass), 2025, 2026
- [ ] Pricing set in portal — **Free** listing + $199/yr Pro via hydrocomplete.com checkout

### Upload

- [ ] `validation-preflight.ps1` + `release.ps1` on final version
- [ ] Upload `dist/HydroComplete-{version}.zip`
- [ ] Pass Autodesk automated manifest validation
- [ ] Submit for review (~1–2 weeks)

---

## Phase 6 — Parallel release paths

You can ship **before** Autodesk approval:

| Path | Ready when | Owner |
|------|------------|-------|
| **Waitlist beta zip** | Phase 1 Blocks A–F pass | 👤 |
| **Direct download** (`install.ps1` + email) | Same | 👤 |
| **Autodesk Marketplace** | Phases 1–5 complete | 👤 ⏳ |

---

## Phase 7 — Post-launch (👤)

- [ ] App Store link on `hydrocomplete.com/civil3d`
- [ ] Email waitlist with download URL + beta token instructions
- [ ] Monitor `support@hydrocomplete.com`
- [ ] Update `README.md` User validation table from Phase 1 results
- [ ] File issues for any Phase 1 failures

---

## Quick reference

| Resource | Path / URL |
|----------|------------|
| Validation session | `dist/app-store/VALIDATION_SESSION.md` |
| Listing copy | `dist/app-store/LISTING.md` |
| Screenshots | `dist/app-store/SCREENSHOTS.md` |
| Captions | `dist/app-store/SCREENSHOT_CAPTIONS.md` |
| Automated gate | `scripts/validation-preflight.ps1` |
| Sign DLLs | `scripts/sign-release.ps1` |
| Licensing API | `hc-refactored/server/routes/licensing.js` |
| Privacy | https://hydrocomplete.com/privacy.html |
| Product page | https://hydrocomplete.com/civil3d |
