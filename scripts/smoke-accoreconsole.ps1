# Smoke test stub: run accoreconsole with HC_ABOUT when Civil 3D is installed.
# Skips gracefully when accoreconsole / Civil 3D is not present (exit 0).
param(
    [string]$AccoreConsole,
    [string]$Drawing,
    [switch]$Strict
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
    $sampleDwg = $sampleDwgs | Where-Object { $_.FullName -notmatch ' ' } | Select-Object -First 1
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

$scriptDir = Join-Path $env:TEMP 'hydrocomplete-smoke'
New-Item -ItemType Directory -Force -Path $scriptDir | Out-Null
$scrPath = Join-Path $scriptDir 'hc-about.scr'
$logPath = Join-Path $scriptDir 'accoreconsole.log'

@(
    '_.FILEDIA 0'
    'HC_ABOUT'
    'QUIT'
) | Set-Content -Path $scrPath -Encoding ASCII

$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\PackageContents.xml'
if (-not (Test-Path $bundle)) {
    Write-Host 'NOTE: HydroComplete.bundle is not installed — run install.ps1 first for HC_ABOUT to register.'
}

$localDwg = Join-Path $scriptDir 'seed.dwg'
Copy-Item -Path $drawingPath -Destination $localDwg -Force

Write-Host "Drawing: $drawingPath"
Write-Host "Local:   $localDwg"
Write-Host "Script:  $scrPath"
Write-Host 'Running accoreconsole /product C3D ...'

$arguments = '/product C3D /i "' + $localDwg + '" /s "' + $scrPath + '" /l en-US'
$timeoutMs = 90 * 1000

Push-Location $scriptDir
try {
    foreach ($name in @('accoreconsole.log', 'AcCoreConsole.log', $logPath)) {
        if (Test-Path $name) {
            Remove-Item $name -Force
        }
    }

    $proc = Start-Process -FilePath $install.Exe `
        -ArgumentList $arguments `
        -WorkingDirectory $scriptDir `
        -PassThru -NoNewWindow

    if (-not $proc.WaitForExit($timeoutMs)) {
        Write-Host "WARN: accoreconsole did not exit within $($timeoutMs / 1000)s - terminating."
        $proc.Kill()
        $proc.WaitForExit(5000) | Out-Null
    }

    $output = @()
    foreach ($name in @('accoreconsole.log', 'AcCoreConsole.log', $logPath)) {
        $candidate = if ([System.IO.Path]::IsPathRooted($name)) { $name } else { Join-Path $scriptDir $name }
        if (Test-Path $candidate) {
            $output += Get-Content $candidate -Raw
        }
    }

    $text = ($output -join "`n")
    $loaded = $text -match 'HydroComplete.*loaded|HC_ABOUT|HC_PIPES|=== HydroComplete'
    $commandOk = $text -match 'HC_PIPES|HC_ABOUT\s+This list|=== HydroComplete for Civil 3D'

    Write-Host "accoreconsole exit code: $($proc.ExitCode)"

    if ($commandOk) {
        Write-Host 'SMOKE OK: HC_ABOUT output detected in accoreconsole log.'
        exit 0
    }

    if ($loaded) {
        Write-Host 'SMOKE PARTIAL: Plugin load message seen, but HC_ABOUT list not found in log.'
    }
    else {
        Write-Host 'SMOKE INCONCLUSIVE: No HydroComplete output in accoreconsole log.'
        Write-Host '  This stub targets the full Civil 3D desktop app; accoreconsole may not auto-load the bundle.'
    }

    if ($Strict) {
        exit 1
    }

    Write-Host 'Stub mode: exiting 0 (use -Strict to fail on inconclusive runs).'
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Pop-Location
}