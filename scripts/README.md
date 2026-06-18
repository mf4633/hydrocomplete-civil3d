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

## Related scripts (repo root)

| Script | Purpose |
|---|---|
| `install.ps1` | Build + copy to `%APPDATA%\Autodesk\ApplicationPlugins\` for local testing |
| `verify-install.ps1` | Check installed bundle vs latest build |
| `package.sh` | Bash equivalent of the DLL copy step (no zip) |