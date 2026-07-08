# HydroComplete — Architecture Overview

A technical map of the system for engineers and technical-diligence readers. It
spans two repositories that share a product but ship independently.

## Products & repositories

| Repo | What it is | Stack |
|---|---|---|
| `hydrocomplete-civil3d` (this repo) | The Civil 3D desktop add-in + a pure calc engine | C# / .NET (netstandard2.0 engine, net8/net48 plugin) |
| `hc-refactored` | The web app (browser calc tool + marketing site) and the Node backend (licensing, Stripe, Supabase, AI) | JS (Vite/webpack front end, Express server) |

The two share **product identity and the licensing backend**, not code. The Civil
3D plugin has its own C# engine; the web app has its own JS engine. Both validate
Pro licenses against the same server (`hc-refactored/server/routes/licensing.js`).

## Civil 3D add-in — two-assembly design

```
HydroComplete.Engine   (netstandard2.0)  — pure hydraulics/hydrology, zero CAD deps
        ▲
        │ project reference
        │
HydroComplete.Civil3D  (net8.0-windows; optional net48)  — ribbon, HC_* commands,
                                                            drawing readers, dialogs
```

- **`HydroComplete.Engine`** targets **netstandard2.0 on purpose**: the *same*
  compiled engine loads into every Civil 3D runtime — 2024/.NET Framework 4.8,
  2025–2026/.NET 8 — with no rebuild. It has no Autodesk dependency, so it builds
  and unit-tests on any machine (this is what CI runs). Every result carries a
  `Steps` trace (`CalcStep`: label, value, units, formula) — the "show your work"
  data the reports render.
- **`HydroComplete.Civil3D`** is the thin host layer: ribbon UI, the 52 `HC_*`
  commands, the readers that pull pipe-network/catchment geometry out of the
  drawing, and WPF dialogs. It references the Autodesk host assemblies
  (`AcMgd`, `AcDbMgd`, `AeccDbMgd`, …) with `Private=false` — they're never copied;
  the plugin binds to them inside the running AutoCAD process.

### Multi-version targeting

| Civil 3D | Series | Host runtime | Plugin TFM | Bundle path |
|---|---|---|---|---|
| 2024 | R24.3 | .NET Framework 4.8 | `net48` | `Contents/net48/*.dll` |
| 2025 | R25.0 | .NET 8 | `net8.0-windows` | `Contents/*.dll` |
| 2026 | R25.1 | .NET 8 | `net8.0-windows` | `Contents/*.dll` |

`PackageContents.xml` ships one `ComponentEntry` per series with matching
`RuntimeRequirements` (`SeriesMin`/`SeriesMax`). The net8 target compiles against a
local Civil 3D install; the net48 target also compiles **offline** against the
`AutoCAD.NET` 24.3.0 + `Civil3D2024.Base` NuGet API stubs (this is what CI does —
see below).

## Licensing flow

```
User buys Pro (Stripe on hydrocomplete.com)
        │  webhook mints hc_live_* token, emails it
        ▼
Civil 3D: HC_ACTIVATE  ──POST──▶  hydrocomplete.com/api/licensing/validate
        │                              │ (HMAC-signed access token, constant-time verify)
        │  ◀── valid + accessToken ────┘
        ▼
%APPDATA%\HydroComplete\license.json  (1-yr expiry, 30-day refresh)
        │
        ▼
LicenseGate.IsProEnabled() gates HC_REPORT_PDF (and future Pro commands)
```

- **Offline grace:** if the server is unreachable, a local stub accepts well-formed
  `hc_live_*` tokens so field laptops keep working; `HC_LICENSE` reports whether
  validation was `online` or `offline-stub`.
- **Server store:** valid keys are provisioned from environment
  (`HC_LICENSE_STORE_JSON` / `HC_LICENSE_KEYS` / `HC_OPENCAD_LICENSE_KEYS`) or
  Supabase — **never hardcoded**. An empty store fails closed (unknown keys → 403).
- **Token integrity:** access tokens are HMAC-signed and verified constant-time
  (`crypto.timingSafeEqual`); the token payload is split on its last `.` because the
  payload itself contains an ISO timestamp.

## Backend (hc-refactored/server)

Express API on Fly.io, fronted by a Netlify proxy at `hydrocomplete.com/api/*`.

- **Licensing** — validate / check-access / status (above).
- **Stripe** — plugin + web checkout; webhook signature verified with the raw body;
  subscription status hardened against email enumeration.
- **Supabase** — auth, projects, analytics (service-role key server-side only).
- **AI assistant** — server-side report drafting (Anthropic), email-gated.
- **Hardening** — constant-time secret comparisons everywhere; sensitive
  query-string values redacted from logs; error details gated to dev; CORS
  allowlist; per-route rate limits; helmet security headers.

## Web app (hc-refactored front end)

A browser-based version of the same calc surface (model-builder canvas, BMP
palette, hydrograph/compliance/report flow) built with Vite/webpack, plus the
marketing site. It has its own JS engines (`src/engines/*`) implementing the same
governing methods, unit-tested with Vitest (500+ tests).

## Build, test & release

| Task | Command | Runs where |
|---|---|---|
| Engine unit tests | `dotnet test tests/HydroComplete.Engine.Tests` | Any machine / CI (ubuntu) |
| Plugin net48 compile | `dotnet build …-p:BuildNet48=true -f net48` | Any Windows (offline NuGet stubs) / CI |
| Plugin net8 build | `.\scripts\preflight-net8.ps1` | Machine with Civil 3D |
| Full bundle + zip | `.\scripts\release.ps1` | Machine with Civil 3D |
| Signing | `.\scripts\sign-release.ps1` (needs cert) | Machine with cert |
| App Store preflight | `.\scripts\app-store-preflight.ps1 [-RequireSigning]` | Any Windows |

**CI** (`.github/workflows/ci.yml`) runs two jobs without a CAD install: an
**engine** job (build + unit tests on ubuntu) and a **plugin** job (net48 offline
compile on windows). The net8 target requires Civil 3D and is built at release
time on a self-hosted runner or the developer's box.

## Why this shape

- **Shared, dependency-free engine** → one binary across Civil 3D versions,
  testable off a CAD machine, and portable to other hosts (the OpenCAD SKU reuses
  the same engine).
- **Thin host layer** → the CAD-specific surface is small and isolated, so version
  bumps (new Civil 3D releases) touch only the host and the manifest.
- **Server-side licensing with offline grace** → central control and revenue
  capture without breaking field use when the network is down.

*Civil 3D and AutoCAD are trademarks of Autodesk, Inc. HydroComplete is an
independent product, not affiliated with or endorsed by Autodesk.*
