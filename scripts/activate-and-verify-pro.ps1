# Activate Pro via HC_ACTIVATE (COM) and verify HC_LICENSE + HC_REPORT_PDF.
param(
    [string]$Email = "michaelbflynn@gmail.com",
    [string]$Token = "hc_live_beta_tester01",
    [string]$AcadExe = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe",
    [string]$Drawing,
    [int]$StartupWaitSec = 45
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$work = Join-Path $env:TEMP 'hydrocomplete-activate'
$licensePath = Join-Path $env:APPDATA 'HydroComplete\license.json'

if (-not $Drawing) {
    $Drawing = 'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
}
if (-not (Test-Path $AcadExe)) { throw "Civil 3D not found: $AcadExe" }
if (-not (Test-Path $Drawing)) { throw "Drawing not found: $Drawing" }

New-Item -ItemType Directory -Force -Path $work | Out-Null
$openDwg = $Drawing
if ($Drawing.StartsWith('C:\Program Files\', [StringComparison]::OrdinalIgnoreCase)) {
    $localDwg = Join-Path $work 'activate-seed.dwg'
    Copy-Item $Drawing $localDwg -Force
    $openDwg = $localDwg
}

Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo', '/i', "`"$openDwg`"" -WindowStyle Normal
Write-Host '[1/4] Launched Civil 3D'

$acad = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try {
        $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
        if ($acad -and $acad.Documents.Count -gt 0) { break }
    } catch {}
}
if (-not $acad) { throw 'COM unavailable after 180s' }
Write-Host "[2/4] COM ready - $($acad.ActiveDocument.Name)"
Start-Sleep -Seconds $StartupWaitSec

$dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
if (-not (Test-Path $dll)) {
    $dll = Join-Path $repoRoot 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
}

& (Join-Path $repoRoot 'scripts\activate-license.ps1') -Email $Email -Token $Token

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 10) {
    for ($try = 0; $try -lt 30; $try++) {
        try {
            $acad.ActiveDocument.SendCommand($cmd)
            Write-Host "  $label"
            Start-Sleep -Seconds $waitSec
            return
        } catch {
            Start-Sleep -Milliseconds 1500
        }
    }
    throw "SendCommand failed: $label"
}

Send-Cmd 'SECURELOAD' "_.SECURELOAD`n0`n" 3
Send-Cmd 'NETLOAD' "NETLOAD`n`"$dll`"`n" 12
Send-Cmd 'HC_LICENSE' "HC_LICENSE`n" 8
Send-Cmd 'HC_REPORT' "HC_REPORT`n" 22
Send-Cmd 'HC_REPORT_PDF' "HC_REPORT_PDF`n" 30

Write-Host '[3/4] Checking license file and PDF output...'
if (-not (Test-Path $licensePath)) { throw "license.json missing: $licensePath" }
$lic = Get-Content $licensePath -Raw | ConvertFrom-Json
Write-Host "  license.json email: $($lic.email)"
Write-Host "  validation: $($lic.validationMode)"

$docs = [Environment]::GetFolderPath('MyDocuments')
$hc = Join-Path $docs 'HydroComplete'
$cutoff = (Get-Date).AddMinutes(-15)
$pdf = Get-ChildItem $hc -Filter 'report-*.pdf' -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -gt $cutoff } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $pdf) { throw "No PDF in $hc" }
Write-Host "  PDF: $($pdf.FullName) ($($pdf.Length) bytes)"

Write-Host '[4/4] ACTIVATE + PDF VERIFY OK'
Start-Process $pdf.FullName