# Build scripts

PowerShell scripts for CI validation and App Store release packaging.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (7+ for tests, 8+ for the Civil 3D plugin)
- **Autodesk Civil 3D 2025 or 2026** (or AutoCAD with matching `AcMgd.dll` paths) for `dotnet build` on `HydroComplete.Civil3D`
- Windows (PowerShell 5.1+)

Override host DLL paths when building against Civil 3D 2025:

```powershell
dotnet build src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj -c Release `
  -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"
```

## CI (`ci.ps1`)

Runs unit tests, builds the plugin, and verifies `dist/HydroComplete.bundle/PackageContents.xml` lists Civil 3D **R25.0** and **R25.1** plus all `HC_*` commands.

```powershell
# From repo root
.\scripts\ci.ps1

# Debug configuration
.\scripts\ci.ps1 -Configuration Debug
```

Exits **0** on success, **non-zero** on failure.

### What runs

| Step | Needs Civil 3D install? | Notes |
|---|---|---|
| `dotnet test` | No (engine tests only) | Solution includes `HydroComplete.Engine` unit tests |
| `dotnet build` | **Yes** (API DLLs on disk) | References `AcMgd.dll`, `AeccDbMgd.dll`, etc. via `AcadDir` |
| Manifest verification | No | Static XML check under `dist/HydroComplete.bundle/` |

CI does **not** start `acad.exe`; compile-time references are enough.

### GitHub Actions

The workflow in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) calls `ci.ps1`.

**Hosted runners:** `windows-latest` has the .NET SDK (via `setup-dotnet`) but **not**
Autodesk Civil 3D. The build step will fail unless host assemblies are present.

**Self-hosted runners:** Use a Windows machine with Civil 3D installed. Full setup
checklist: **[setup-self-hosted-runner.md](setup-self-hosted-runner.md)**.

Summary:

1. Install and register a self-hosted runner with labels `self-hosted`, `Windows`, and `civil3d`
2. Change workflow `runs-on` to `[self-hosted, Windows, civil3d]` (see comments in `ci.yml`)
3. Confirm `.\scripts\ci.ps1` passes on the runner machine before relying on CI

If CI is unavailable, run `.\scripts\ci.ps1` locally before tagging a release.

#### Default host paths (`HydroComplete.Civil3D.csproj`)

| Install | `AcadDir` default |
|---|---|
| Civil 3D 2026 | `C:\Program Files\Autodesk\AutoCAD 2026\` |
| Civil 3D 2025 | Override with `-p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025\"` |

Related managed DLLs resolved from `$(AcadDir)` and `$(AcadDir)C3D\`.

## App Store preflight (`app-store-preflight.ps1`)

**No-Civil-3D release gate** — tests, CI, preflight, HTTP smoke (civil3d page + licensing API):

```powershell
.\scripts\validation-preflight.ps1
.\scripts\validation-preflight.ps1 -SkipHttp   # offline
```

**Code signing** (after cert obtained):

```powershell
$env:HC_SIGN_CERT_THUMBPRINT = 'YOUR_THUMBPRINT'
.\scripts\sign-release.ps1
.\scripts\release.ps1
```

Validates bundle readiness before Publisher upload:

- `PackageIcon.png` exists in `Contents/`
- Every `[CommandMethod("HC_*")]` in source appears in `PackageContents.xml` (and vice versa)
- Version strings match across csproj, manifest, `Plugin.cs`, `verify-install.ps1`, and `LISTING.md`
- Release zip structure (when `dist/HydroComplete-{version}.zip` exists)
- R24.3 manifest entry required only when `Contents/net48/` is present

```powershell
.\scripts\app-store-preflight.ps1
```

Exits **0** on success, **non-zero** on failure. Run before `release.ps1` on each submission.

## Version bump (`bump-version.ps1`)

Syncs release version across csproj, manifest, startup banner, and listing copy:

```powershell
.\scripts\bump-version.ps1 -Version 1.2.0
```

Updates: `HydroComplete.Civil3D.csproj`, `HydroComplete.Engine.csproj`, `PackageContents.xml` `AppVersion`, `Plugin.cs` banner, `verify-install.ps1`, `LISTING.md`, and `HC_ABOUT` header.

## Release (`release.ps1`)

Builds Release, refreshes `dist/HydroComplete.bundle/Contents/` DLLs, and zips the bundle for Autodesk App Store upload.

```powershell
# Version from src/HydroComplete.Civil3D/HydroComplete.Civil3D.csproj (<Version>)
.\scripts\release.ps1

# Explicit version (zip name only; does not rewrite csproj or manifest)
.\scripts\release.ps1 -Version 0.3.2
```

Output:

- `dist/HydroComplete-{version}.zip` — zip root contains `PackageContents.xml` and `Contents/` (bundle folder contents)
- SHA256 hash printed to the console

Before first release, ensure `dist/HydroComplete.bundle/Contents/PackageIcon.png` exists (96×96 PNG).

## Civil 3D parity smoke (`smoke-civil3d-parity.ps1`)

Automated v1.4.0 check via COM: launches Civil 3D 2026, runs `HC_REPORT`, `HC_NETWORK_DIAGRAM`, and `HC_SOIL`, then verifies HTML outputs under `Documents\HydroComplete\` (respects OneDrive folder redirection).

```powershell
# After install.ps1 — closes any open acad.exe by default
.\scripts\smoke-civil3d-parity.ps1

# Custom drawing (e.g. C-STORM)
.\scripts\smoke-civil3d-parity.ps1 -Drawing "D:\Projects\C-STORM.dwg"

# Leave your open Civil 3D session alone (may fail if COM is busy)
.\scripts\smoke-civil3d-parity.ps1 -KeepExistingAcad
```

- **Exit 0** when KaTeX report + network-diagram HTML are written and pass content checks.
- **Exit 1** on failure; **exit 0 skip** when Civil 3D 2026 or seed drawing is missing.

## accoreconsole smoke test (`smoke-accoreconsole.ps1`)

Optional headless check that runs `accoreconsole /product C3D` with `HC_ABOUT` when Civil 3D is installed locally. **Not wired into `ci.ps1` or GitHub Actions.**

```powershell
# From repo root (after install.ps1)
.\scripts\smoke-accoreconsole.ps1

# Fail if plugin output is missing
.\scripts\smoke-accoreconsole.ps1 -Strict

# Override paths
.\scripts\smoke-accoreconsole.ps1 -AccoreConsole "C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe" -Drawing "C:\path\to\seed.dwg"
```

Behavior:

- **Exit 0** when Civil 3D / `accoreconsole` is not found (graceful skip for machines without Autodesk installs).
- **Exit 0** in default stub mode when the run is inconclusive; use **`-Strict`** to exit non-zero if `HC_ABOUT` output is not detected.
- **Exit 0** when `HC_ABOUT` command list appears in the accoreconsole log.

### Limitations

| Topic | Detail |
|---|---|
| **Supported host** | HydroComplete is built for the **full Civil 3D desktop app** (`acad.exe`). `accoreconsole` is experimental for this stub only. |
| **Auto-load bundle** | ApplicationPlugins bundles may not load in headless `accoreconsole` the same way as interactive startup. Run `install.ps1` first; manual `NETLOAD` is not attempted here. |
| **Command scope** | Only `HC_ABOUT` is invoked — safe without pipe networks or catchments. Other `HC_*` commands need drawing data and UI. |
| **Seed drawing** | Requires a template DWG/DWT under the AutoCAD install, or pass **`-Drawing`**. Without one, the script skips. |
| **CI / release gate** | Informational stub only; does not block builds or App Store packaging. Use interactive Civil 3D for real validation. |
| **Log parsing** | Success is inferred from `accoreconsole` log text, not from AutoCAD editor echo in a GUI session. |

## Related scripts (repo root)

| Script | Purpose |
|---|---|
| `install.ps1` | Build + copy to `%APPDATA%\Autodesk\ApplicationPlugins\` for local testing |
| `verify-install.ps1` | Check installed bundle vs latest build |
| `package.sh` | Bash equivalent of the DLL copy step (no zip) |