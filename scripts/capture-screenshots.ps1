# Capture Autodesk App Store listing screenshots per dist/app-store/SCREENSHOTS.md.
# Stages a seeded-catchment drawing (HC_TEST_SEED_CATCHMENTS) so routed-Q,
# analyze, and pre/post shots show real per-catchment hydrology, then runs each
# command and captures REAL output windows via PrintWindow (no focus needed).
# Output: dist/app-store/screenshots/*.png
param(
    [string]$Drawing,
    [string]$AcadExe,
    [int]$StartupWaitSec = 40
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$work = Join-Path $env:TEMP 'hydrocomplete-screenshots'
$outDir = Join-Path $repoRoot 'dist\app-store\screenshots'
$bundleDll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'
$seedDll = Join-Path $repoRoot 'tests\HydroComplete.TestSeed\bin\Release\net8.0-windows\HydroComplete.TestSeed.dll'

if (-not $AcadExe) { $AcadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe' }
if (-not (Test-Path $AcadExe)) { Write-Host 'SKIP: Civil 3D 2026 not found'; exit 2 }
if (-not $Drawing) {
    $Drawing = 'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
}
if (-not (Test-Path $seedDll)) { Write-Host "SKIP: build TestSeed first ($seedDll)"; exit 2 }

New-Item -ItemType Directory -Force -Path $work, $outDir | Out-Null
$localDwg = Join-Path $work 'storm-demo.dwg'
Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
Copy-Item $Drawing $localDwg -Force
Get-ChildItem $work -Filter 'storm-demo.dwl*' -ErrorAction SilentlyContinue | Remove-Item -Force

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public delegate bool EnumProc(IntPtr h, IntPtr lp);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
Add-Type -AssemblyName System.Drawing

function Find-WindowByTitle([string]$titlePart, [int]$targetPid = 0) {
    $found = [IntPtr]::Zero
    $cb = [Win+EnumProc]{
        param($h, $lp)
        if (-not [Win]::IsWindowVisible($h)) { return $true }
        $procId = 0
        [Win]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
        if ($targetPid -ne 0 -and $procId -ne $targetPid) { return $true }
        $sb = New-Object System.Text.StringBuilder 512
        [Win]::GetWindowText($h, $sb, 512) | Out-Null
        if ($sb.ToString() -like "*$titlePart*") {
            $script:foundHwnd = $h
            return $false
        }
        return $true
    }
    $script:foundHwnd = [IntPtr]::Zero
    [Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:foundHwnd
}

function Capture-Window([IntPtr]$hwnd, [string]$path) {
    if ($hwnd -eq [IntPtr]::Zero) { throw 'null hwnd' }
    $r = New-Object Win+RECT
    [Win]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { throw "bad rect $w x $h" }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    # PW_RENDERFULLCONTENT (2) captures composited/DirectX content.
    [Win]::PrintWindow($hwnd, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  captured $(Split-Path $path -Leaf)"
}

Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo' -WindowStyle Maximized | Out-Null
Write-Host '[1/4] Launched Civil 3D'
$acad = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try { $acad = [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application'); if ($acad) { break } } catch {}
}
if (-not $acad) { throw 'COM unavailable' }
Start-Sleep -Seconds $StartupWaitSec
while ($acad.Documents.Count -gt 0) {
    try { $acad.Documents.Item(0).Close($false) } catch { break }
    Start-Sleep -Milliseconds 500
}
$opened = $false
for ($try = 0; $try -lt 40; $try++) {
    try { $null = $acad.Documents.Open($localDwg); $opened = $true; break } catch { Start-Sleep -Seconds 2 }
}
if (-not $opened) { throw 'Documents.Open failed' }
Start-Sleep -Seconds 5

$acadProc = Get-Process acad | Select-Object -First 1
$mainHwnd = $acadProc.MainWindowHandle
[Win]::ShowWindow($mainHwnd, 3) | Out-Null   # SW_MAXIMIZE
Write-Host "[2/4] COM v$($acad.Version) - $($acad.ActiveDocument.Name)"

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 6) {
    for ($try = 0; $try -lt 40; $try++) {
        try {
            $script:acad.ActiveDocument.SendCommand($cmd)
            Write-Host "  $label"
            Start-Sleep -Seconds $waitSec
            return
        } catch { Start-Sleep -Milliseconds 1500 }
    }
    throw "SendCommand failed: $label"
}

function Capture-TextWindow([string]$label, [string]$cmd, [string]$file, [int]$waitSec = 8) {
    Send-Cmd $label $cmd $waitSec
    Send-Cmd 'TEXTSCR' "_.TEXTSCR`n" 3
    $txtHwnd = Find-WindowByTitle 'Text Window' $acadProc.Id
    if ($txtHwnd -eq [IntPtr]::Zero) { throw 'text window not found' }
    Capture-Window $txtHwnd (Join-Path $outDir $file)
    Send-Cmd 'GRAPHSCR' "_.GRAPHSCR`n" 2
}

Write-Host '[3/4] Staging + capturing'
Send-Cmd 'SECURELOAD' "_.SECURELOAD`n0`n" 3
Send-Cmd 'NETLOAD bundle' "NETLOAD`n`"$bundleDll`"`n" 10
Send-Cmd 'NETLOAD seeder' "NETLOAD`n`"$seedDll`"`n" 5
Send-Cmd 'seed catchments' "HC_TEST_SEED_CATCHMENTS`n" 8
Send-Cmd 'zoom extents' "_.ZOOM`n_E`n" 4

$shots = New-Object System.Collections.Generic.List[string]
function Try-Shot([string]$name, [scriptblock]$action) {
    try { & $action; $shots.Add($name) } catch { Write-Host "  FAIL ${name}: $_" }
}

# 01: main window, plan view, HydroComplete ribbon tab (UIA-select the tab).
Try-Shot '01-ribbon-tab.png' {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($mainHwnd)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'HydroComplete')
    $tab = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($tab) {
        try {
            $sel = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $sel.Select()
            Start-Sleep -Seconds 2
        } catch {}
    }
    Capture-Window $mainHwnd (Join-Path $outDir '01-ribbon-tab.png')
}

# 02-03: capacity tables (routed per-catchment Q).
Try-Shot '02-hc-pipes-output.png' {
    Capture-TextWindow 'HC_PIPES' "HC_PIPES`n" '02-hc-pipes-output.png' 8
}
Try-Shot '03-hc-capacity-output.png' {
    Capture-TextWindow 'HC_CAPACITY (routed)' "HC_CAPACITY`n`n`n" '03-hc-capacity-output.png' 15
}

# 04: HGL with labels + profile polyline, then zoom to show geometry.
Try-Shot '04-hc-hgl-profile.png' {
    Send-Cmd 'HC_PIPES_WRITE' "HC_PIPES_WRITE`n" 10
    Send-Cmd 'HC_HGL' "HC_HGL`n`n`n`n`n`n`n" 30
    Send-Cmd 'zoom extents' "_.ZOOM`n_E`n" 4
    Capture-Window $mainHwnd (Join-Path $outDir '04-hc-hgl-profile.png')
}

# 05-07 + 6a + 09: command-output shots.
Try-Shot '05-hc-analyze.png' {
    Capture-TextWindow 'HC_ANALYZE' "HC_ANALYZE`n`n`n`nN`n" '05-hc-analyze.png' 25
}
Try-Shot '06-hc-detention.png' {
    Capture-TextWindow 'HC_DETENTION' ("HC_DETENTION`n" + ("`n" * 17)) '06-hc-detention.png' 20
}
Try-Shot '06a-hc-gvf.png' {
    Capture-TextWindow 'HC_GVF' "HC_GVF`n`n`n`n`n`nNormal`n`n`n`n`n`nDone`n`n" '06a-hc-gvf.png' 12
}
Try-Shot '07-hc-prepost.png' {
    Capture-TextWindow 'HC_PREPOST' "HC_PREPOST`n`n`n`n" '07-hc-prepost.png' 15
}
Try-Shot '09-hc-atlas14-idf.png' {
    Capture-TextWindow 'HC_ATLAS14' "HC_ATLAS14`n" '09-hc-atlas14-idf.png' 6
}
Try-Shot '10-hc-license.png' {
    Capture-TextWindow 'HC_LICENSE' "HC_LICENSE`n" '10-hc-license.png' 5
}

# 08: HTML report in a NEW browser window (closed afterwards, only that window).
Try-Shot '08-hc-report-browser.png' {
    Send-Cmd 'HC_REPORT' "HC_REPORT`n`n`n`nN`n" 20
    $docs = [Environment]::GetFolderPath('MyDocuments')
    $report = Get-ChildItem (Join-Path $docs 'HydroComplete') -Filter 'report-storm-demo-*.html' |
        Where-Object { $_.Name -notmatch 'network-diagram|analysis' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $report) { throw 'report html not found' }
    Start-Process msedge -ArgumentList '--new-window', ('"' + $report.FullName + '"')
    Start-Sleep -Seconds 8
    $edge = Find-WindowByTitle 'HydroComplete'
    if ($edge -eq [IntPtr]::Zero) { $edge = Find-WindowByTitle 'report-storm-demo' }
    if ($edge -eq [IntPtr]::Zero) { throw 'browser window not found' }
    [Win]::ShowWindow($edge, 3) | Out-Null
    Start-Sleep -Seconds 3
    Capture-Window $edge (Join-Path $outDir '08-hc-report-browser.png')
    [Win]::SendMessage($edge, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE that window only
}

# 13: DAG visual model builder palette (LAST - WebView2 wedges COM afterwards).
Try-Shot '13-hc-dag-builder.png' {
    Send-Cmd 'HC_DAG' "HC_DAG`n" 20
    Capture-Window $mainHwnd (Join-Path $outDir '13-hc-dag-builder.png')
}

Write-Host '[4/4] Done'
Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host ("Captured {0} shot(s) -> {1}" -f $shots.Count, $outDir)
$shots | ForEach-Object { Write-Host "  $_" }
if ($shots.Count -lt 8) { exit 1 }
exit 0
