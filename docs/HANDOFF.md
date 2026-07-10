# HydroComplete for Civil 3D — Dev Handoff & Review Package

_Single entry point for reviewing the project's current state and the July 2026 work.
Everything below is on `master`._

---

## State at a glance

| | |
|---|---|
| **Version** | v1.7.2 · 52 `HC_*` commands · Civil 3D 2024 / 2025 / 2026 |
| **Engine tests** | ✅ **420 passed, 0 failed, 1 skipped** (`dotnet test`, verified this session) |
| **Engine Release build** | ✅ clean — 0 warnings, 0 errors (`netstandard2.0`) |
| **Deep audit** | ✅ 31 verified findings → **all 30 distinct addressed** (`docs/AUDIT-2026-07.md`) |
| **Marketing / listing copy** | ✅ consistent, corrected (52-cmd table, pricing, no "CI green" claim) |
| **App Store submission** | ⬜ paperwork done; build+sign, live validation, screenshots, portal still pending |
| **Civil 3D plugin build** | ⚠️ not buildable off a Civil 3D box (needs Autodesk managed refs) |

---

## What was done this engagement

1. **UI / manifest polish** — `HC_ABOUT` net8-only notes; DAG command consistency.
2. **Marketing/PR copy audit** — added the missing `HC_WQ_DIAGRAM` to the listing's command
   table (was 51 of 52); removed an inaccurate "CI green" claim from three docs.
3. **Deep engineering audit** — a 12-lens, adversarially-verified sweep of the calculation
   engine + plugin (79 agents). 31 findings verified; **all 30 distinct addressed** across
   correctness, robustness, crash-safety, and security. See `docs/AUDIT-2026-07.md`.
4. **Regression suite + `dotnet test` validation** — added `AuditRegressionTests.cs` and
   license-denial locks; ran the full engine suite, which caught a self-introduced regression
   (F29) and a Clark-UH volume defect — both fixed. Suite now green.

Merged PRs: **#1** (HC_ABOUT), **#8** (marketing copy), **#9** (engine audit), **#10** (test fixes).

---

## Document index

### Submission & launch (`dist/app-store/`)
| File | Purpose | Audience |
|---|---|---|
| `README.md` | Launch-kit map + one-screen plan | You |
| `LAUNCH-PLAN.md` | Sequenced critical path + 90-day post-launch | You |
| `SUBMISSION_CHECKLIST.md` | Line-item status tracker (phases 0–7) | You |
| `PORTAL-FIELDS.md` | Autodesk Publisher portal field-by-field runbook | You |
| `LISTING.md` | All store copy: title, descriptions, 52-command table, pricing, keywords, release notes, legal | Portal |
| `LAUNCH-COMMS.md` | Waitlist email, LinkedIn, forum, ADN outreach | Public |
| `SCREENSHOTS.md` / `SCREENSHOT_CAPTIONS.md` | The 10-shot list + captions | You (on a C3D box) |
| `VALIDATION_SESSION.md` | On-machine Civil 3D functional validation script | You (on a C3D box) |

### Product & company (`docs/`)
| File | Purpose | Audience |
|---|---|---|
| `ONE-PAGER.md` | Positioning + the Autodesk ask | Partners / Autodesk |
| `ARCHITECTURE.md` | Engine + plugin + backend technical overview | Technical diligence |
| `USER-GUIDE.md` | Install, activate, workflows, methods | End users |
| `COMMERCIAL.md` | Free/Pro model and path to paid | You |
| **`AUDIT-2026-07.md`** | **The deep-audit report — all 31 findings, fixes, and post-merge test validation** | **You (review first)** |
| `HANDOFF.md` | This document | You |

### Root
| File | Purpose |
|---|---|
| `README.md` | Repo overview + validation table |

---

## Build & test

```bash
# Engine (pure netstandard2.0 — builds & tests anywhere with the .NET SDK)
dotnet build src/HydroComplete.Engine/HydroComplete.Engine.csproj -c Release
dotnet test  tests/HydroComplete.Engine.Tests/HydroComplete.Engine.Tests.csproj
#   → 420 passed, 0 failed, 1 skipped

# Civil 3D plugin (needs Autodesk managed assemblies on the machine)
#   Net8AcadDir / Net48AcadDir resolve to the installed AutoCAD/Civil 3D paths.
dotnet build src/HydroComplete.Civil3D/HydroComplete.Civil3D.csproj -c Release

# Full signed bundle + zip (on a Civil 3D box, per scripts/)
scripts/release.ps1        # assemble net8 + net48 bundle -> dist/HydroComplete-1.7.2.zip
scripts/sign-release.ps1   # sign binaries (needs the code-signing cert)
scripts/app-store-preflight.ps1 -RequireSigning   # final gate, must exit 0
```

---

## Outstanding work (nothing blocking on the engine)

**Before submission (all need a Civil 3D box / the portal — see `SUBMISSION_CHECKLIST.md`):**
- Order the **code-signing certificate** (the long pole — 1–10 business days).
- Build + sign the **net8 bundle**; produce `dist/HydroComplete-1.7.2.zip`.
- **Live-validate all 52 commands** in Civil 3D (`VALIDATION_SESSION.md`).
- Capture **screenshots** (1920×1080, client data scrubbed).
- Run `python scripts/check-licensing-proxy.py` from a network that can reach `hydrocomplete.com`.
- Fill the **Autodesk Publisher portal** and submit.

**Two engine items worth a sanity check on the next test run (both are correctness-sensitive and
changed here):**
- **F9** — the Muskingum-Cunge routing rework (algorithm change).
- **F4** — the culvert critical-head ratio (incl. the `Hc/D ≤ 1` cap).

**Security note carried from `LAUNCH-PLAN.md`:** rotate the three leaked keys still in git
history (OpenAI + Supabase anon + service_role) and provision the license keys as Fly secrets
before the backend/repo changes hands.
