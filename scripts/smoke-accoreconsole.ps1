# Headless smoke: run accoreconsole /product C3D, NETLOAD the installed bundle DLL,
# and assert real command output (HC_ABOUT, HC_NETWORK, HC_PIPES).
# accoreconsole never auto-loads ApplicationPlugins bundles, so NETLOAD is explicit.
# Skips gracefully (exit 0) when Civil 3D is not present; fails (exit 1) on missing markers.
param(
    [string]$AccoreConsole,
    [string]$Drawing,
    [int]$TimeoutSec = 240,
    [switch]$Lenient
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Find-Civil3dAccoreConsole {
    if ($AccoreConsole) {
        if (-not (Test-Path $AccoreConsole)) {
            throw "AccoreConsole not found: $AccoreConsole"
        }
        return @{
            Exe  = (Resolve-Path $AccoreConsole).Path
            Root = Split-Path -Parent $AccoreConsole
        }
    }

    foreach ($year in @('2026', '2025')) {
        foreach ($rootPath in @(
                "C:\Program Files\Autodesk\AutoCAD $year",
                "C:\Program Files\Autodesk\AutoCAD Civil 3D $year"
            )) {
            $exe = Join-Path $rootPath 'accoreconsole.exe'
            $c3d = Join-Path $rootPath 'C3D'
            if ((Test-Path $exe) -and (Test-Path $c3d)) {
                return @{ Exe = $exe; Root = $rootPath }
            }
        }
    }

    return $null
}

function Find-SeedDrawing {
    param([string]$InstallRoot)

    if ($Drawing) {
        if (-not (Test-Path $Drawing)) {
            throw "Drawing not found: $Drawing"
        }
        return (Resolve-Path $Drawing).Path
    }

    # Prefer a drawing with a real pipe network so HC_NETWORK / HC_PIPES exercise the readers.
    $tutorial = Join-Path $InstallRoot 'C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
    if (Test-Path $tutorial) {
        return $tutorial
    }

    $templates = @(
        (Join-Path $InstallRoot 'Template\acad.dwt'),
        (Join-Path $InstallRoot 'Template\acad.dwg')
    )
    foreach ($path in $templates) {
        if (Test-Path $path) {
            return $path
        }
    }

    $sampleDwgs = Get-ChildItem -Path (Join-Path $InstallRoot 'Sample') -Filter '*.dwg' -Recurse -ErrorAction SilentlyContinue
    $sampleDwg = $sampleDwgs | Where-Object { $_.Name -notmatch ' ' } | Select-Object -First 1
    if (-not $sampleDwg) {
        $sampleDwg = $sampleDwgs | Select-Object -First 1
    }
    if ($sampleDwg) {
        return $sampleDwg.FullName
    }

    return $null
}

$install = Find-Civil3dAccoreConsole
if (-not $install) {
    Write-Host 'SKIP: Civil 3D / accoreconsole not found (checked AutoCAD 2025/2026 under Program Files\Autodesk).'
    exit 0
}

Write-Host "Found accoreconsole: $($install.Exe)"

$drawingPath = Find-SeedDrawing -InstallRoot $install.Root
if (-not $drawingPath) {
    Write-Host 'SKIP: No seed drawing/template found for accoreconsole (pass -Drawing path/to.dwg).'
    exit 0
}

$dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
if (-not (Test-Path $dll)) {
    Write-Host 'NOTE: HydroComplete.bundle not installed - falling back to build output. Run install.ps1 for the real thing.'
    $dll = Join-Path $root 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
}
if (-not (Test-Path $dll)) {
    Write-Host "SKIP: No HydroComplete.Civil3D.dll found (install.ps1 or build first)."
    exit 0
}

$scriptDir = Join-Path $env:TEMP 'hydrocomplete-smoke'
New-Item -ItemType Directory -Force -Path $scriptDir | Out-Null
$scrPath = Join-Path $scriptDir 'hc-smoke.scr'
$outPath = Join-Path $scriptDir 'accoreconsole-stdout.log'

# No blank lines: a blank line = Enter = repeat-last-command at a quiescent prompt.
# HC_ABOUT / HC_NETWORK / HC_PIPES take no prompts on a catchment-free drawing.
@(
    '_.FILEDIA 0'
    '_.SECURELOAD 0'
    ('NETLOAD "' + $dll + '"')
    'HC_ABOUT'
    'HC_NETWORK'
    'HC_PIPES'
    '_.QUIT'
    '_Y'
) | Set-Content -Path $scrPath -Encoding ASCII

$localDwg = Join-Path $scriptDir 'seed.dwg'
Copy-Item -Path $drawingPath -Destination $localDwg -Force

Write-Host "Drawing: $drawingPath"
Write-Host "DLL:     $dll"
Write-Host "Script:  $scrPath"
Write-Host 'Running accoreconsole /product C3D ...'

$arguments = '/product C3D /i "' + $localDwg + '" /s "' + $scrPath + '" /l en-US'

Push-Location $scriptDir
try {
    if (Test-Path $outPath) {
        Remove-Item $outPath -Force
    }

    $proc = Start-Process -FilePath $install.Exe `
        -ArgumentList $arguments `
        -WorkingDirectory $scriptDir `
        -PassThru -NoNewWindow `
        -RedirectStandardOutput $outPath

    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        Write-Host "NOTE: accoreconsole still alive after ${TimeoutSec}s - terminating (markers may already be captured)."
        $proc.Kill()
        $proc.WaitForExit(5000) | Out-Null
    }

    # accoreconsole writes UTF-16LE to stdout; a plain ANSI read yields NUL-interleaved text.
    $text = ''
    if (Test-Path $outPath) {
        $text = [System.IO.File]::ReadAllText($outPath, [System.Text.Encoding]::Unicode)
        if ($text -match "`0") {
            $text = $text -replace "`0", ''
        }
    }

    $checks = [ordered]@{
        'NETLOAD (no load error)' = ($text -notmatch 'Unable to load|Unknown command "NETLOAD"')
        'HC_ABOUT command list'   = ($text -match 'HC_ABOUT\s+This list')
        'HC_NETWORK summary'      = ($text -match 'pipe network summary')
        'HC_PIPES Manning table'  = ($text -match 'Manning capacity')
    }

    Write-Host "accoreconsole exit code: $($proc.ExitCode)"
    $failed = @()
    foreach ($kv in $checks.GetEnumerator()) {
        $status = if ($kv.Value) { 'PASS' } else { 'FAIL' }
        Write-Host "  $($kv.Key): $status"
        if (-not $kv.Value) { $failed += $kv.Key }
    }

    if ($failed.Count -eq 0) {
        Write-Host 'SMOKE OK: headless HC_ABOUT/HC_NETWORK/HC_PIPES verified via accoreconsole.'
        exit 0
    }

    Write-Host "SMOKE FAIL: $($failed -join '; ')"
    Write-Host "Full output: $outPath"
    if ($Lenient) {
        Write-Host 'Lenient mode: exiting 0 despite failures.'
        exit 0
    }
    exit 1
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}
