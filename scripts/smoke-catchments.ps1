# Catchment-path headless sweep via accoreconsole /product C3D.
# Seeds 3 catchments (distinct area/C/Tc, explicit reference-structure outlets)
# into a copy of the tutorial pipe drawing using the test-only
# HydroComplete.TestSeed DLL, then exercises every catchment-dependent command
# with exact prompt inputs and scoped output assertions.
#
# Build the seeder first:
#   dotnet build tests\HydroComplete.TestSeed\HydroComplete.TestSeed.csproj -c Release
#
# Prompt-count rule (same as smoke-headless-sweep.ps1): send exactly the inputs
# the prompt flow needs; surplus Enters repeat the command, missing ones strand
# the next command name into a pending prompt.
param(
    [string]$AccoreConsole,
    [string]$Drawing,
    [int]$TimeoutSec = 480
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $AccoreConsole) {
    $AccoreConsole = 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe'
}
if (-not (Test-Path $AccoreConsole)) {
    Write-Host "SKIP: accoreconsole not found at $AccoreConsole"
    exit 0
}
if (-not $Drawing) {
    $Drawing = 'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
}
if (-not (Test-Path $Drawing)) {
    Write-Host "SKIP: seed drawing not found: $Drawing"
    exit 0
}
$dll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
if (-not (Test-Path $dll)) {
    Write-Host 'SKIP: bundle not installed (run install.ps1 first).'
    exit 0
}
$seedDll = Join-Path $root 'tests\HydroComplete.TestSeed\bin\Release\net8.0-windows\HydroComplete.TestSeed.dll'
if (-not (Test-Path $seedDll)) {
    Write-Host "SKIP: TestSeed DLL not built: $seedDll"
    exit 0
}

# Catchment-present prompt flows (3 catchments, 1 network, no geolocation).
# DesignFlowResolver now asks: Route catchment flows <Yes> + Preset key <charlotte-nc>.
$cases = @(
    @{ Cmd = 'HC_TEST_SEED_CATCHMENTS'; Inputs = @();                Marker = 'seeded 3 catchments in group HC-Test' }
    # Core hydraulics with routed catchment flows.
    @{ Cmd = 'HC_RATIONAL';        Inputs = @('');                   Marker = 'PEAK FLOW Q = [\d.]+ cfs' }
    @{ Cmd = 'HC_CAPACITY';        Inputs = @('', '');               Marker = 'design capacity check \(routed Q' }
    @{ Cmd = 'HC_CAPACITY_WRITE';  Inputs = @('', '', '');           Marker = 'wrote \d+ capacity label\(s\)' }
    @{ Cmd = 'HC_HGL';             Inputs = @('', '', '', '', '', ''); Marker = 'Wrote HGL labels to \d+ pipe\(s\)' }
    @{ Cmd = 'HC_REPORT';          Inputs = @('', '', '', 'N');      Marker = 'HTML report written' }
    @{ Cmd = 'HC_ANALYZE';         Inputs = @('', '', '', 'N');      Marker = 'analysis report written' }
    @{ Cmd = 'HC_TC';              Inputs = @('', '', '', '', '', '', 'Done', ''); Marker = 'TOTAL Tc = [\d.]+ min' }
    # Stormwater / hydrology compute paths.
    @{ Cmd = 'HC_SCS';             Inputs = @('', '');               Marker = 'SCS CN runoff \([\s\S]{0,80}?3 catchments\)' }
    @{ Cmd = 'HC_UNIT_HYDRO';      Inputs = @();                     Marker = 'SCS unit hydrograph \(A=[\d.]+ ac' }
    @{ Cmd = 'HC_SEDIMENT';        Inputs = @('', '', '', '');       Marker = 'RUSLE soil loss' }
    @{ Cmd = 'HC_WQV';             Inputs = @('');                   Marker = 'WQV = \d+ cf' }
    @{ Cmd = 'HC_DETENTION';       Inputs = @('', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', ''); Marker = 'Q_in,peak = [\d.]+ cfs' }
    @{ Cmd = 'HC_BMP_SIZE';        Inputs = @('', '', '');           Marker = 'BMP sizing \(' }
    @{ Cmd = 'HC_WQ_TRAIN';        Inputs = @('', '', 'bioretention,wet-pond', ''); Marker = 'BMP treatment train \(' }
    @{ Cmd = 'HC_SEDIMENT_BASIN';  Inputs = @('', '', '', '', '', '', ''); Marker = 'sediment basin \([\s\S]{0,60}?Q=[\d.]+ cfs\)' }
    @{ Cmd = 'HC_BIORETENTION';    Inputs = @('', '', '', '', '', ''); Marker = 'bioretention routing \(' }
    @{ Cmd = 'HC_WETLAND';         Inputs = @('', '', '');           Marker = 'constructed wetland routing \(' }
    @{ Cmd = 'HC_HYDROGRAPH';      Inputs = @('', '', '');           Marker = 'design hydrograph \(A=[\d.]+ ac' }
    @{ Cmd = 'HC_ROUTE_HYDRO';     Inputs = @('', '', '');           Marker = 'CSV exported' }
    @{ Cmd = 'HC_PREPOST';         Inputs = @('', '', '');           Marker = 'Overall: (PASS|FAIL) \(\d+ of \d+ storms' }
    @{ Cmd = 'HC_OPTIMIZE';        Inputs = @('');                   Marker = 'treatment-train optimization' }
    @{ Cmd = 'HC_MULTIRP';         Inputs = @('');                   Marker = 'pipe\(s\) surcharged at 100-yr design Q' }
    @{ Cmd = 'HC_WQ_DIAGRAM';      Inputs = @('', '', 'BioretentionCellRainGarden', 'Done'); Marker = 'treatment train diagram' }
    # Live SSURGO path: typed lat/lon, 8s timeout with regional fallback either way.
    @{ Cmd = 'HC_SOIL';            Inputs = @('Live', '', '', '');   Marker = "suitability: (Excellent|Good|Marginal|Poor|NotRecommended)" }
)

$work = Join-Path $env:TEMP 'hydrocomplete-catchments'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$scrPath = Join-Path $work 'catchments.scr'
$outPath = Join-Path $work 'catchments-stdout.log'
$localDwg = Join-Path $work 'catch-seed.dwg'
Copy-Item $Drawing $localDwg -Force

$scrLines = New-Object System.Collections.Generic.List[string]
$scrLines.Add('_.FILEDIA 0')
$scrLines.Add('_.SECURELOAD 0')
$scrLines.Add('NETLOAD "' + $dll + '"')
$scrLines.Add('NETLOAD "' + $seedDll + '"')
foreach ($case in $cases) {
    $scrLines.Add($case.Cmd)
    foreach ($line in $case.Inputs) { $scrLines.Add($line) }
}
$scrLines.Add('_.QUIT')
$scrLines.Add('_Y')
$scrLines | Set-Content -Path $scrPath -Encoding ASCII

Write-Host "Catchment sweep: $($cases.Count) commands against $([System.IO.Path]::GetFileName($Drawing))"
if (Test-Path $outPath) { Remove-Item $outPath -Force }

$arguments = '/product C3D /i "' + $localDwg + '" /s "' + $scrPath + '" /l en-US'
$proc = Start-Process -FilePath $AccoreConsole -ArgumentList $arguments `
    -WorkingDirectory $work -PassThru -NoNewWindow -RedirectStandardOutput $outPath

if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    Write-Host "NOTE: accoreconsole still alive after ${TimeoutSec}s - terminating."
    $proc.Kill()
    $proc.WaitForExit(5000) | Out-Null
}

$text = [System.IO.File]::ReadAllText($outPath, [System.Text.Encoding]::Unicode)
if ($text -match "`0") { $text = $text -replace "`0", '' }

$passed = 0
$failed = @()
foreach ($case in $cases) {
    $scoped = 'Command: ' + $case.Cmd + '\b[\s\S]{0,12000}?' + $case.Marker
    if ($text -match $scoped) {
        Write-Host "  PASS $($case.Cmd)"
        $passed++
    }
    else {
        Write-Host "  FAIL $($case.Cmd)  (marker: $($case.Marker))"
        $failed += $case.Cmd
    }
}

if ($text -match 'System\.\w+Exception') {
    Write-Host '  WARN: .NET exception text present in log - inspect even if markers passed.'
}

Write-Host ''
Write-Host "Catchment sweep: $passed/$($cases.Count) passed. Log: $outPath"
if ($failed.Count -eq 0) {
    Write-Host 'SWEEP OK'
    exit 0
}
Write-Host "SWEEP FAIL: $($failed -join ', ')"
exit 1
