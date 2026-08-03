# Comprehensive headless command sweep via accoreconsole /product C3D.
# Runs every headless-safe HC_* command against the Civil 3D tutorial pipe drawing
# (1 network, 11 pipes, 12 structures, 0 catchments, 0 surfaces, no geolocation)
# with EXACT prompt inputs, then asserts a per-command output marker scoped to
# that command's echo in the captured stdout.
#
# Prompt-count rule: send exactly as many input lines as the command's prompt
# flow needs for this drawing state. A surplus Enter after completion repeats
# the command (AutoCAD repeat-last-command); a missing Enter strands the next
# command's name into the pending prompt.
#
# Excluded (GUI): HC_NETWORK_EDIT, HC_INLETS, HC_PROFILE, HC_BACKGROUND, HC_DAG,
# HC_DAG_SAVE, HC_DAG_LOAD. The DAG trio hangs accoreconsole even on its early-exit
# paths (JIT touches PaletteSet/WebView2 types with no GUI available) - desktop only.
param(
    [string]$AccoreConsole,
    [string]$Drawing,
    [int]$TimeoutSec = 420
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

# Each case: command, exact input lines after the command, success-marker regex.
# Markers are matched inside a window that starts at this command's echo.
$cases = @(
    @{ Cmd = 'HC_ABOUT';           Inputs = @();                     Marker = '=== HydroComplete for Civil 3D' }
    @{ Cmd = 'HC_LICENSE';         Inputs = @();                     Marker = '=== HydroComplete License ===' }
    @{ Cmd = 'HC_ATLAS14';         Inputs = @();                     Marker = 'NOAA Atlas 14 IDF' }
    @{ Cmd = 'HC_NETWORK';         Inputs = @();                     Marker = 'pipe network summary \(1 network\(s\)\)' }
    @{ Cmd = 'HC_PIPES';           Inputs = @();                     Marker = 'Manning capacity \(11 pipes\)' }
    @{ Cmd = 'HC_CAPACITY';        Inputs = @('');                   Marker = 'design capacity check \(Q=10\.0 cfs, 11 pipes\)' }
    @{ Cmd = 'HC_CAPACITY_WRITE';  Inputs = @('', '');               Marker = 'wrote \d+ capacity label\(s\)' }
    @{ Cmd = 'HC_PIPES_WRITE';     Inputs = @();                     Marker = 'wrote capacity to 11 pipe\(s\)' }
    @{ Cmd = 'HC_HGL';             Inputs = @('', '', '', '', '');   Marker = 'Wrote HGL labels to \d+ pipe\(s\) on layer HC-HGL' }
    @{ Cmd = 'HC_VALIDATE';        Inputs = @('');                   Marker = 'design validation \(Q=10\.0 cfs, 11 pipes\)' }
    @{ Cmd = 'HC_REVIEW';          Inputs = @('', '', '');           Marker = 'HydroComplete: design review \(' }
    @{ Cmd = 'HC_SIZE';            Inputs = @('');                   Marker = 'standard pipe sizing \(Q=10\.0 cfs, 11 pipes\)' }
    @{ Cmd = 'HC_RATIONAL';        Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_ANALYZE';         Inputs = @();                     Marker = 'No catchments found' }
    @{ Cmd = 'HC_ACTIVATE';        Inputs = @('');                   Marker = 'Activation cancelled' }
    @{ Cmd = 'HC_REPORT';          Inputs = @('', '', 'N');          Marker = 'HTML report written' }
    @{ Cmd = 'HC_REPORT_PDF';      Inputs = @('', '', 'N');          Marker = '(PDF report written|PDF export is a Pro feature)' }
    # Catchment-guarded compute commands: correct empty-state on this drawing.
    @{ Cmd = 'HC_SCS';             Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_UNIT_HYDRO';      Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_SEDIMENT';        Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_WQV';             Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_DETENTION';       Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_BMP_SIZE';        Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_WQ_TRAIN';        Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_SEDIMENT_BASIN';  Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_BIORETENTION';    Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_WETLAND';         Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_HYDROGRAPH';      Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_PREPOST';         Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_OPTIMIZE';        Inputs = @();                     Marker = 'No catchments found in this drawing' }
    @{ Cmd = 'HC_ROUTE_HYDRO';     Inputs = @();                     Marker = 'No catchments found' }
    @{ Cmd = 'HC_MULTIRP';         Inputs = @();                     Marker = 'requires catchments for Rational routing' }
    # Fully-exercisable compute commands.
    @{ Cmd = 'HC_TC';              Inputs = @('', '', '', '', '', '', 'Done'); Marker = 'TOTAL Tc = [0-9.]+ min' }
    @{ Cmd = 'HC_SOIL';            Inputs = @('', 'cecil-sandy-loam', '');    Marker = 'soil lookup[\s\S]{0,400}?HSG: B' }
    @{ Cmd = 'HC_GVF';             Inputs = @('', '', '', '', '', 'Normal', '', '', '', '', '', 'Done', ''); Marker = 'GVF profile' }
    @{ Cmd = 'HC_LOSS';            Inputs = @('', '', '', '', '', ''); Marker = 'Total excess:\s+[\d.]+ in' }
    # Wetland BMP exercises the constructed-wetland continuous-routing path.
    @{ Cmd = 'HC_CONTINUOUS';      Inputs = @('', '', '', '', '', 'Wetland', ''); Marker = 'continuous simulation \(' }
    @{ Cmd = 'HC_COST';            Inputs = @('');                   Marker = 'pipe cost estimate' }
    @{ Cmd = 'HC_CULVERT';         Inputs = @('', '1', '', '');      Marker = 'culvert headwater' }
    # Export first, then import round-trips on the default path (plain Enter).
    @{ Cmd = 'HC_LANDXML';         Inputs = @('');                   Marker = 'LandXML export' }
    @{ Cmd = 'HC_LANDXML_IMPORT';  Inputs = @('', '');               Marker = 'Import preview only' }
    @{ Cmd = 'HC_NETWORK_DIAGRAM'; Inputs = @('');                   Marker = 'HydroComplete: network diagram' }
    @{ Cmd = 'HC_PROFILE_DXF';     Inputs = @('', '', '', '', '');   Marker = 'profile DXF export' }
    @{ Cmd = 'HC_WQ_DIAGRAM';      Inputs = @('', '', '', 'BioretentionCellRainGarden', 'Done'); Marker = 'treatment train diagram' }
    @{ Cmd = 'HC_PUMP';            Inputs = @();                     Marker = 'No pump structures found' }
)

$work = Join-Path $env:TEMP 'hydrocomplete-sweep'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$scrPath = Join-Path $work 'sweep.scr'
$outPath = Join-Path $work 'sweep-stdout.log'
$localDwg = Join-Path $work 'seed.dwg'
Copy-Item $Drawing $localDwg -Force

# Remove a stale round-trip file so HC_LANDXML_IMPORT proves THIS run's export.
$docs = [Environment]::GetFolderPath('MyDocuments')
$roundTrip = Join-Path $docs 'HydroComplete\seed_network.xml'
if (Test-Path $roundTrip) { Remove-Item $roundTrip -Force }

$scrLines = New-Object System.Collections.Generic.List[string]
$scrLines.Add('_.FILEDIA 0')
$scrLines.Add('_.SECURELOAD 0')
$scrLines.Add('NETLOAD "' + $dll + '"')
foreach ($case in $cases) {
    $scrLines.Add($case.Cmd)
    foreach ($line in $case.Inputs) { $scrLines.Add($line) }
}
$scrLines.Add('_.QUIT')
$scrLines.Add('_Y')
$scrLines | Set-Content -Path $scrPath -Encoding ASCII

Write-Host "Sweep: $($cases.Count) commands against $([System.IO.Path]::GetFileName($Drawing))"
Write-Host "DLL:   $dll"
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
    $scoped = 'Command: ' + $case.Cmd + '\b[\s\S]{0,9000}?' + $case.Marker
    if ($text -match $scoped) {
        Write-Host "  PASS $($case.Cmd)"
        $passed++
    }
    else {
        Write-Host "  FAIL $($case.Cmd)  (marker: $($case.Marker))"
        $failed += $case.Cmd
    }
}

# A command that crashed leaves a .NET exception dump in the log.
if ($text -match 'System\.\w+Exception') {
    Write-Host '  WARN: .NET exception text present in log - inspect even if markers passed.'
}

Write-Host ''
Write-Host "Sweep: $passed/$($cases.Count) passed. Log: $outPath"
if ($failed.Count -eq 0) {
    Write-Host 'SWEEP OK'
    exit 0
}
Write-Host "SWEEP FAIL: $($failed -join ', ')"
exit 1
