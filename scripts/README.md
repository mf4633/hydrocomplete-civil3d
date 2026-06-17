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

### GitHub Actions

The workflow in `.github/workflows/ci.yml` calls `ci.ps1` on `windows-latest`. The **build step requires Civil 3D / AutoCAD managed assemblies** on the runner (`C:\Program Files\Autodesk\AutoCAD 2026\` by default). Use a self-hosted Windows runner with Civil 3D installed, or run `ci.ps1` locally before tagging a release.

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