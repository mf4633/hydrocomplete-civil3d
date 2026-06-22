# NETLOAD + SECURELOAD fallback when the ApplicationPlugins bundle is locked or missing.
param(
    [string]$Dll,
    [string]$AcadExe,
    [int]$WaitSec = 12
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Dll) {
    $Dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
}
if (-not (Test-Path $Dll)) {
    $Dll = Join-Path $root 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
}
if (-not (Test-Path $Dll)) {
    throw "HydroComplete.Civil3D.dll not found. Run install.ps1 first."
}
if (-not $AcadExe) {
    $AcadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'
}
if (-not (Test-Path $AcadExe)) {
    throw "Civil 3D not found at $AcadExe"
}

$acad = $null
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    try {
        $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
        if ($acad -and $acad.Documents.Count -gt 0) { break }
    } catch {}
}
if (-not $acad) {
    Write-Host "No running Civil 3D session — launching..."
    Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo' -WindowStyle Normal | Out-Null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 3
        try {
            $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
            if ($acad -and $acad.Documents.Count -gt 0) { break }
        } catch {}
    }
}
if (-not $acad) { throw 'COM unavailable — open Civil 3D manually and re-run.' }

function Send-Cmd([string]$label, [string]$cmd, [int]$wait = 8) {
    $acad.ActiveDocument.SendCommand($cmd)
    Write-Host "  $label"
    Start-Sleep -Seconds $wait
}

Write-Host "NETLOAD fallback: $Dll"
Send-Cmd 'SECURELOAD 0' "_.SECURELOAD`n0`n" 3
Send-Cmd 'NETLOAD' "NETLOAD`n`"$Dll`"`n" $WaitSec
Send-Cmd 'HC_ABOUT' "HC_ABOUT`n" 6
Write-Host "NETLOAD fallback complete — check command line for HydroComplete banner."