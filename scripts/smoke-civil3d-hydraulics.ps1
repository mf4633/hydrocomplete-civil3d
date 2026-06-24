# Automated hydraulics smoke: HC_PIPES_WRITE, HC_CAPACITY, HC_HGL.
# Verifies MText on HC-CAPACITY / HC-HGL and profile geometry on HC-HGL-PROFILE via COM.
# Requires Civil 3D 2026 desktop + install.ps1 bundle.
param(
    [string]$Drawing,
    [string]$AcadExe,
    [switch]$KeepExistingAcad,
    [int]$StartupWaitSec = 45,
    [int]$MinCapacityLabels = 3,
    [int]$MinHglLabels = 3,
    [int]$MinProfileEntities = 1
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$work = Join-Path $env:TEMP 'hydrocomplete-hydraulics-smoke'
$bundleDll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'

if (-not $AcadExe) {
    $AcadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'
}
if (-not (Test-Path $AcadExe)) {
    Write-Host "SKIP: Civil 3D 2026 not found at $AcadExe"
    exit 2
}

if (-not $Drawing) {
    $candidates = @(
        'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg',
        (Join-Path $repoRoot 'test-fixtures\Pipe Networks-3.dwg')
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            $Drawing = $path
            break
        }
    }
}
if (-not $Drawing -or -not (Test-Path $Drawing)) {
    Write-Host 'SKIP: No seed drawing found. Pass -Drawing path\to\storm.dwg (e.g. C-STORM).'
    exit 2
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$seedName = [System.IO.Path]::GetFileNameWithoutExtension($Drawing)
$localDwg = Join-Path $work "$seedName-hydraulics-smoke.dwg"
Copy-Item $Drawing $localDwg -Force

if (-not $KeepExistingAcad) {
    Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

# Blank launch avoids session-restore replacing /i with the last user drawing.
Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo' -WindowStyle Normal | Out-Null
Write-Host "[1/6] Launched Civil 3D (seed: $([System.IO.Path]::GetFileName($Drawing)))"

$acad = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try {
        $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
        if ($acad) { break }
    } catch {}
}
if (-not $acad) { throw 'COM unavailable after 180s' }
Start-Sleep -Seconds $StartupWaitSec

$targetLeaf = [System.IO.Path]::GetFileName($localDwg)
while ($acad.Documents.Count -gt 0) {
    try { $acad.Documents.Item(0).Close($false) } catch { break }
    Start-Sleep -Milliseconds 500
}
$openedDoc = $false
for ($try = 0; $try -lt 40; $try++) {
    try {
        $null = $acad.Documents.Open($localDwg)
        $openedDoc = $true
        break
    } catch {
        Start-Sleep -Seconds 2
    }
}
if (-not $openedDoc) { throw "Documents.Open failed for $localDwg" }
$activeName = ''
for ($try = 0; $try -lt 40; $try++) {
    Start-Sleep -Seconds 2
    try {
        $activeName = [string]$acad.ActiveDocument.Name
        if ($activeName -eq $targetLeaf) { break }
    } catch {}
}
if ($activeName -ne $targetLeaf) {
    throw "Wrong drawing active: '$activeName' (expected $targetLeaf)"
}
Write-Host "[2/6] COM v$($acad.Version) - $activeName"

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 8) {
    for ($try = 0; $try -lt 40; $try++) {
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

function Get-LayerEntityCounts([object]$doc, [string[]]$layers) {
    $counts = @{}
    foreach ($layer in $layers) { $counts[$layer] = 0 }

    foreach ($blockName in @('ModelSpace', 'PaperSpace')) {
        try {
            $space = $doc.$blockName
            $total = $space.Count
            for ($i = 0; $i -lt $total; $i++) {
                try {
                    $ent = $space.Item($i)
                    $layer = [string]$ent.Layer
                    if ($counts.ContainsKey($layer)) {
                        $counts[$layer]++
                    }
                } catch {}
            }
        } catch {}
    }

    return $counts
}

function Wait-LayerCount {
    param(
        [object]$Doc,
        [string]$Layer,
        [int]$MinCount,
        [int]$MaxAttempts = 6,
        [int]$SleepSec = 4,
        [string]$Context = ''
    )
    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        $counts = Get-LayerEntityCounts $Doc @($Layer)
        if ($counts[$Layer] -ge $MinCount) {
            return $counts[$Layer]
        }
        Start-Sleep -Seconds $SleepSec
    }
    Send-Cmd 'REGEN' "_.REGEN`n" 3
    return (Get-LayerEntityCounts $Doc @($Layer))[$Layer]
}

$dll = $bundleDll
if (-not (Test-Path $dll)) {
    Write-Host 'WARN: Bundle DLL missing - run install.ps1 first. NETLOAD from build output.'
    $dll = Join-Path $repoRoot 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
}

Send-Cmd 'SECURELOAD' "_.SECURELOAD`n0`n" 3
Send-Cmd 'NETLOAD' "NETLOAD`n`"$dll`"`n" 15
Send-Cmd 'HC_NETWORK' "HC_NETWORK`n" 10
Send-Cmd 'HC_PIPES' "HC_PIPES`n" 18

$before = Get-LayerEntityCounts $acad.ActiveDocument @('HC-CAPACITY', 'HC-HGL', 'HC-HGL-PROFILE')
Write-Host "[3/6] Layer counts before write-back: HC-CAPACITY=$($before['HC-CAPACITY']), HC-HGL=$($before['HC-HGL']), HC-HGL-PROFILE=$($before['HC-HGL-PROFILE'])"

# No catchments: uniform Q defaults to 10 cfs on Enter.
Send-Cmd 'HC_PIPES_WRITE' "HC_PIPES_WRITE`n" 20
$capacityLabels = Wait-LayerCount -Doc $acad.ActiveDocument -Layer 'HC-CAPACITY' -MinCount $MinCapacityLabels
Write-Host "  HC-CAPACITY labels after HC_PIPES_WRITE: $capacityLabels"

$pollAttempts = [Math]::Min(30, 6 + [Math]::Ceiling($capacityLabels / 8))
$scaledWait = [Math]::Min(240, 15 + [Math]::Ceiling($capacityLabels * 0.5))
Send-Cmd 'HC_CAPACITY' "HC_CAPACITY`n`n" $scaledWait

# HC_HGL prompts (no catchments): Q, HEC-22 Yes, momentum No, tailwater(s), profile Yes.
$hglWait = [Math]::Min(600, 45 + [Math]::Ceiling($capacityLabels * 1.5))
Write-Host "  (scaled waits: capacity=${scaledWait}s, hgl=${hglWait}s, poll=$pollAttempts)"
Send-Cmd 'HC_HGL' "HC_HGL`n`n`n`n`n`n`n`n`n`n" $hglWait
$hglLabels = Wait-LayerCount -Doc $acad.ActiveDocument -Layer 'HC-HGL' -MinCount $MinHglLabels -MaxAttempts $pollAttempts
$profileCount = Wait-LayerCount -Doc $acad.ActiveDocument -Layer 'HC-HGL-PROFILE' -MinCount $MinProfileEntities -MaxAttempts $pollAttempts

if ($acad.ActiveDocument.Name -ne $targetLeaf) {
    throw "Active drawing changed during smoke: $($acad.ActiveDocument.Name) (expected $targetLeaf)"
}
$after = Get-LayerEntityCounts $acad.ActiveDocument @('HC-CAPACITY', 'HC-HGL', 'HC-HGL-PROFILE')
Write-Host "[4/6] Layer counts after commands:"
Write-Host "  HC-CAPACITY:     $($after['HC-CAPACITY'])"
Write-Host "  HC-HGL:          $($after['HC-HGL'])"
Write-Host "  HC-HGL-PROFILE:  $($after['HC-HGL-PROFILE'])"

$capacityAfter = $after['HC-CAPACITY']
$checks = [ordered]@{
    'HC_PIPES_WRITE labels (HC-CAPACITY)' = ($capacityLabels -ge $MinCapacityLabels)
    'HC-CAPACITY labels retained'         = ($capacityAfter -ge $MinCapacityLabels)
    'HC_CAPACITY dispatched'              = $true
    'HC_HGL labels (HC-HGL)'              = ($hglLabels -ge $MinHglLabels)
    'HC_HGL profile (HC-HGL-PROFILE)'     = ($profileCount -ge $MinProfileEntities)
}

Write-Host '[5/6] Checks'
$failed = @()
foreach ($kv in $checks.GetEnumerator()) {
    $status = if ($kv.Value) { 'PASS' } else { 'FAIL' }
    Write-Host "  $($kv.Key): $status"
    if (-not $kv.Value) { $failed += $kv.Key }
}

if ($failed.Count -eq 0) {
    Write-Host "[6/6] SMOKE OK: hydraulics write-back verified on $([System.IO.Path]::GetFileName($Drawing))"
    exit 0
}

Write-Host "[6/6] SMOKE FAIL: $($failed -join '; ')"
exit 1