# Building the net8 Civil 3D plugin (the shippable deliverable)

CI compiles the plugin's **net48** target offline against NuGet API stubs, which
proves the code is sound. The **net8.0-windows** target — the one that ships for
Civil 3D 2025/2026 — needs the real Autodesk managed assemblies and therefore a
machine with Civil 3D installed. This page is the one-command path to produce it.

For a permanently green net8 build in CI, wire a self-hosted runner instead —
see [setup-self-hosted-runner.md](setup-self-hosted-runner.md).

## One command

On a box with Civil 3D 2026 installed:

```powershell
cd C:\path\to\hydrocomplete-civil3d
.\scripts\preflight-net8.ps1
```

For Civil 3D **2025** or a non-default install path:

```powershell
.\scripts\preflight-net8.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2025\"
```

The script verifies the prerequisites, builds `Release`, and prints the output
DLL path. It exits non-zero (with the specific missing item) if anything is off,
so it doubles as a preflight check before a demo or release.

## Prerequisites (what the script checks)

| Requirement | Notes |
|---|---|
| **.NET SDK 8.0** | https://dotnet.microsoft.com/download |
| **AcMgd.dll, AcCoreMgd.dll, AcDbMgd.dll, AdWindows.dll** | under the AutoCAD install dir (default `C:\Program Files\Autodesk\AutoCAD 2026\`) |
| **C3D\AeccDbMgd.dll** | Civil 3D managed API, under the same install |

## Output

```
src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll
```

Stage it into the loadable bundle with the existing net48 flow's counterpart
(`build-net48.ps1`) or the full release pipeline (`release.ps1`), which also
assembles `dist\HydroComplete.bundle\` with `PackageContents.xml`.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `[X] missing: ...AcMgd.dll` | Civil 3D not installed, or wrong version path | Install C3D 2026, or pass `-AcadDir` for 2025 |
| `[X] .NET 8 SDK not found` | SDK not installed / not on PATH | Install the .NET 8 SDK, reopen the shell |
| Build fails with `CS0246` | Host assemblies resolved to an empty/partial install | Confirm all five DLLs exist; repair the Civil 3D install |
| `acad.exe`-related errors | none expected — this is a **compile**, no CAD launch | If you see them, you're running the wrong script |
