# Autodesk Publisher Portal — Field-by-Field Runbook

Sit in front of the [Publisher portal](https://aps.autodesk.com/app-store/publisher-center)
with this open. Every form field is mapped to the exact source (mostly
[`LISTING.md`](LISTING.md)) plus the gotchas. Copy is written; this is the
mechanical fill-in.

**Legend:** 📋 copy from `LISTING.md` · 📎 upload a file · ⌨️ type/select · ⚠️ gotcha

## App metadata

| Portal field | Source | Notes |
|---|---|---|
| App name / Title | 📋 Title → `HydroComplete for Civil 3D` | Match `PackageContents.xml` `Name` exactly. |
| Short description | 📋 Short Description | **≤ 80 chars** — current copy is 78. Don't add to it. |
| Long description | 📋 Long Description | Portal accepts limited formatting; paste, then fix any bullet rendering. |
| Category | ⌨️ **Civil Engineering** | Autodesk taxonomy; pick the closest if that exact label isn't offered. |
| Keywords / tags | 📋 Keywords | Paste; the portal may cap the count — keep the first ~20 (most specific first). |
| Supported products | ⌨️ Civil 3D **2024, 2025, 2026** | Must match the `RuntimeRequirements` series in the manifest (R24.3/R25.0/R25.1). |
| System requirements | 📋 System Requirements | Windows 10/11 64-bit; host Civil 3D. |
| What's New / release notes | 📋 Release Notes (v1.7.2) | ⚠️ trim to the portal's char limit if it rejects the full block. |
| Version | ⌨️ `1.7.2` | Must equal `AppVersion` in the manifest and `<Version>` in the csproj. |
| Help / product URL | ⌨️ `https://hydrocomplete.com/civil3d` | Matches manifest `HelpFile`. |

## Publisher / legal

| Portal field | Source | Notes |
|---|---|---|
| Company / publisher | ⌨️ `HydroComplete` | Matches manifest `CompanyDetails`. |
| Support email | ⌨️ `support@hydrocomplete.com` | ⚠️ Monitor it from launch day — reviewers and first users use it. |
| Privacy policy URL | ⌨️ `https://hydrocomplete.com/privacy.html` | Must resolve (HTTP 200). |
| EULA / license agreement | ⌨️ desktop add-in terms | `terms.html` §12 on hydrocomplete.com; confirm the portal has the current text. |
| Trademark notice | 📋 Legal / Trademark Notice | Autodesk-independence disclaimer in the footer. |

## Pricing

| Portal field | Value | Notes |
|---|---|---|
| Business model | ⌨️ **Free** listing | Pro is sold off-store (Stripe on hydrocomplete.com) and unlocked by `HC_ACTIVATE`. |
| In-app purchase disclosure | ⌨️ Yes — external Pro upgrade | Disclose that a paid Pro tier exists via the developer site, so review isn't surprised by the gated `HC_REPORT_PDF`. |
| Payout / tax | — | Only needed if you switch to an Autodesk-commerce paid listing later; not for a Free listing. |

## Artifacts to upload

| Item | Source | Notes |
|---|---|---|
| 📎 App bundle (zip) | `dist/HydroComplete-1.7.2.zip` | ⚠️ Built + **signed** on a Civil 3D box via `release.ps1`; the repo doesn't carry the net8 payload. Run `app-store-preflight.ps1 -RequireSigning` first — it must exit 0. |
| 📎 App icon / thumbnail | store listing icon | ⚠️ Distinct from the 96×96 `PackageIcon.png` in the bundle; the portal wants a larger store thumbnail (use the HydroComplete logo). |
| 📎 Screenshots (≥3, up to ~8) | per `SCREENSHOTS.md` | 1920×1080; captions from `SCREENSHOT_CAPTIONS.md`. Scrub client data. |
| 📎 Demo video (optional) | 60–90s | Materially lifts conversion; link a YouTube/Vimeo URL. |

## Pre-submit gate (all must be true)

- [ ] `app-store-preflight.ps1 -RequireSigning` exits 0 (signed, versions in sync, 52 commands, icon present).
- [ ] Zip installs and uninstalls cleanly on a fresh Civil 3D profile.
- [ ] All 52 commands launch without an unhandled exception (reviewers run them).
- [ ] `HC_ACTIVATE` works online **and** the offline stub works with the network off.
- [ ] The three public URLs (product, privacy, support) resolve.
- [ ] License keys provisioned as Fly secrets so activation succeeds during review.

Hit **Submit for review**. Expect ~1–2 weeks; respond to any reviewer note within a business day — turnaround is the main lever on total time-to-live. Sequenced context: [`LAUNCH-PLAN.md`](LAUNCH-PLAN.md).
