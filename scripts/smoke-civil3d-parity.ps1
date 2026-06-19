# Automated v1.4.0 parity smoke: HC_REPORT (KaTeX), HC_NETWORK_DIAGRAM, HC_SOIL.
# Requires Civil 3D 2026 desktop + install.ps1 bundle. Uses COM SendCommand.
# Closes existing acad.exe by default so the script owns the session.
param(
    [string]$Drawing,
    [string]$AcadExe,
    [switch]$KeepExistingAcad,
    [int]$StartupWaitSec = 40
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$work = Join-Path $env:TEMP 'hydrocomplete-smoke'
$dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
$docs = [Environment]::GetFolderPath('MyDocuments')
$hc = Join-Path $docs 'HydroComplete'

if (-not $AcadExe) {
    $AcadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'
}
if (-not (Test-Path $AcadExe)) {
    Write-Host "SKIP: Civil 3D 2026 not found at $AcadExe"
    exit 0
}

if (-not $Drawing) {
    $Drawing = 'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
}
if (-not (Test-Path $Drawing)) {
    Write-Host "SKIP: Drawing not found: $Drawing"
    exit 0
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$localDwg = Join-Path $work 'parity-seed.dwg'
Copy-Item $Drawing $localDwg -Force

if (-not $KeepExistingAcad) {
    Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo', '/i', $localDwg -WindowStyle Normal | Out-Null
Write-Host '[1/5] Launched Civil 3D'

$acad = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try {
        $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
        if ($acad -and $acad.Documents.Count -gt 0) { break }
    } catch {}
}
if (-not $acad) { throw 'COM unavailable after 180s' }
Write-Host "[2/5] COM v$($acad.Version) — $($acad.ActiveDocument.Name)"
Start-Sleep -Seconds $StartupWaitSec

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 8) {
    for ($try = 0; $try -lt 30; $try++) {
        try {
            $script:acad.ActiveDocument.SendCommand($cmd)
            Write-Host "  $label"
            Start-Sleep -Seconds $waitSec
            return
        } catch {
            Start-Sleep -Milliseconds 1500
        }
    }
    throw "SendCommand failed: $label"
}

if (-not (Test-Path $dll)) {
    Write-Host "WARN: Bundle DLL missing — run install.ps1 first. Attempting NETLOAD from build output."
    $dll = Join-Path $repoRoot 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
}
Send-Cmd 'NETLOAD' "NETLOAD`n`"$dll`"`n" 12
Send-Cmd 'HC_PIPES' "HC_PIPES`n" 10
Send-Cmd 'HC_REPORT' "HC_REPORT`n`n`nNo`n" 22
Send-Cmd 'HC_NETWORK_DIAGRAM' "HC_NETWORK_DIAGRAM`nNo`n" 15
Send-Cmd 'HC_SOIL' "HC_SOIL`nName`nCecil`nBioretention`n" 15

Write-Host '[3/5] Polling Documents\HydroComplete...'
$reportHtml = $null
$diagramHtml = $null
for ($i = 0; $i -lt 24; $i++) {
    Start-Sleep -Seconds 5
    if (-not (Test-Path $hc)) { continue }
    $files = Get-ChildItem $hc -File | Sort-Object LastWriteTime -Descending
    $reportHtml = $files | Where-Object { $_.Name -match '^report-.*\.html$' -and $_.Name -notmatch 'network-diagram' } | Select-Object -First 1
    $diagramHtml = $files | Where-Object { $_.Name -match 'network-diagram\.html$' } | Select-Object -First 1
    if ($reportHtml -and $diagramHtml) { break }
}

Write-Host "[4/5] Output folder: $hc"
if (Test-Path $hc) {
    Get-ChildItem $hc | Sort-Object LastWriteTime -Descending | Select-Object -First 6 | ForEach-Object {
        Write-Host "  $($_.Name) ($($_.Length) bytes)"
    }
}

$katexOk = $false
$svgOk = $false
if ($reportHtml) {
    $text = Get-Content $reportHtml.FullName -Raw
    $katexOk = $text -match 'katex@0\.16' -and $text -match 'hc-formula-panel' -and $text -match 'hc-tex-fallback'
}
if ($diagramHtml) {
    $text = Get-Content $diagramHtml.FullName -Raw
    $svgOk = $text -match '<svg' -and $text -match 'Pipe Network Diagram'
}

Write-Host '[5/5] Checks'
Write-Host "  KaTeX report: $(if ($katexOk) { 'PASS' } else { 'FAIL' })"
Write-Host "  Network SVG:  $(if ($svgOk) { 'PASS' } else { 'FAIL' })"
Write-Host "  HC_SOIL:      dispatched (Cecil name lookup — live SSURGO needs drawing geo)"

if ($reportHtml -and $diagramHtml -and $katexOk -and $svgOk) {
    Write-Host 'SMOKE OK: v1.4.0 parity outputs verified.'
    exit 0
}

Write-Host 'SMOKE FAIL: expected HTML report + network diagram in Documents\HydroComplete'
exit 1