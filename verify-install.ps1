# Quick check: is the auto-load bundle installed and current?
$dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle'
$built = 'C:\Users\michael.flynn\dev\hydrocomplete-civil3d\src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
$installed = Join-Path $dest 'Contents\HydroComplete.Civil3D.dll'
$manifest = Join-Path $dest 'PackageContents.xml'

Write-Host "Auto-load folder: $dest"
if (-not (Test-Path $manifest)) {
    Write-Host "STATUS: NOT INSTALLED - run install.ps1 (close Civil 3D first)"
    exit 1
}
Write-Host "Manifest: OK"
$manifestText = Get-Content $manifest -Raw
foreach ($series in @('R25.0', 'R25.1')) {
    if ($manifestText -notmatch "SeriesMin=`"$series`"") {
        Write-Host "MANIFEST: missing ComponentEntry for $series (Civil 3D 2025/2026) - reinstall from dist/HydroComplete.bundle"
        exit 1
    }
    Write-Host "Series: $series OK"
}
if (-not (Test-Path $installed)) {
    Write-Host "STATUS: DLL MISSING - run install.ps1"
    exit 1
}
if (Test-Path $built) {
    $match = (Get-FileHash $installed).Hash -eq (Get-FileHash $built).Hash
    if ($match) { Write-Host "DLL: OK (matches latest build)" }
    else { Write-Host "DLL: STALE - close Civil 3D, run install.ps1, restart" }
} else {
    Write-Host "DLL: present (build output not found for comparison)"
}
Select-String -Path $manifest -Pattern 'LoadOnAutoCADStartup|Platform|AppVersion' | ForEach-Object { Write-Host $_.Line.Trim() }
Write-Host ""
Write-Host "Launch Civil 3D 2025 or 2026 full app (not core console)."
Write-Host "Startup banner: HydroComplete for Civil 3D 0.7.0 loaded"