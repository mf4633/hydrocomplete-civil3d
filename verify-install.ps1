# Quick check: is the auto-load bundle installed and current?
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle'
$built = Join-Path $root 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
$builtNet48 = Join-Path $root 'src\HydroComplete.Civil3D\bin\Release\net48\HydroComplete.Civil3D.dll'
$installed = Join-Path $dest 'Contents\HydroComplete.Civil3D.dll'
$installedNet48 = Join-Path $dest 'Contents\net48\HydroComplete.Civil3D.dll'
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
$net48Staged = Test-Path (Join-Path $root 'dist\HydroComplete.bundle\Contents\net48\HydroComplete.Civil3D.dll')
if ($net48Staged -or (Test-Path $installedNet48)) {
    if ($manifestText -notmatch 'SeriesMin="R24.3"') {
        Write-Host "MANIFEST: missing ComponentEntry for R24.3 (Civil 3D 2024) - rebuild net48 and reinstall"
        exit 1
    }
    Write-Host "Series: R24.3 OK (Civil 3D 2024 net48)"
    if (-not (Test-Path $installedNet48)) {
        Write-Host "net48 DLL: MISSING in install folder - run install.ps1"
        exit 1
    }
    if (Test-Path $builtNet48) {
        $net48Match = (Get-FileHash $installedNet48).Hash -eq (Get-FileHash $builtNet48).Hash
        if ($net48Match) { Write-Host "net48 DLL: OK (matches latest build)" }
        else { Write-Host "net48 DLL: STALE - close Civil 3D, run install.ps1, restart" }
    }
    else {
        Write-Host "net48 DLL: present (build output not found for comparison)"
    }
}
else {
    Write-Host "Series: R24.3 skipped (no net48 build staged)"
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
$dagIndex = Join-Path $dest 'Contents\dag\index.html'
$dagWasm = Join-Path $dest 'Contents\dag\pkg\hydrocomplete_dag_bg.wasm'
$dagJs = Join-Path $dest 'Contents\dag\pkg\hydrocomplete_dag.js'
if ((Test-Path $dagIndex) -and (Test-Path $dagWasm) -and (Test-Path $dagJs)) {
    Write-Host "DAG bundle: OK (index.html + WASM)"
}
else {
    Write-Host "DAG bundle: INCOMPLETE - close Civil 3D, run install.ps1 (dag/pkg missing for HC_DAG)"
}
Select-String -Path $manifest -Pattern 'LoadOnAutoCADStartup|Platform|AppVersion' | ForEach-Object { Write-Host $_.Line.Trim() }
Write-Host ""
Write-Host "Launch Civil 3D 2024, 2025, or 2026 full app (not core console)."
Write-Host "Startup banner: HydroComplete for Civil 3D 1.7.2 loaded"