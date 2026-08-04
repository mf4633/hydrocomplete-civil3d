# Desktop GUI smoke: HC_INLETS, HC_PROFILE, HC_NETWORK_EDIT, HC_BACKGROUND, HC_DAG.
# These need the full Civil 3D app: modal WPF dialogs, GetPoint picks, WebView2 palette.
#
# Dialog strategy (SendKeys is a dead end here): SendCommand BLOCKS until the
# whole command finishes, so a modal dialog deadlocks any script that plans to
# type into it afterwards. Instead:
#   - every Editor prompt answer is PRE-QUEUED inside the SendCommand string;
#   - each modal dialog is dismissed by a UI Automation watchdog job (separate
#     process) that invokes the target button by name - no window focus needed,
#     works even while the user is typing elsewhere.
# Output is captured via LOGFILEMODE/LOGFILEPATH and asserted per command.
#
# Constraints learned the hard way:
# - LOGFILENAME is read-only; set LOGFILEPATH (folder) and find the auto-named log.
# - HC_DAG must be LAST and nothing may be sent after it: once the WebView2
#   palette opens, further SendCommand/COM calls can wedge the session
#   (RPC_E_SYS_CALL_FAILED). HC_DAG_SAVE/HC_DAG_LOAD stay manual-only.
# - The script owns the session: kills acad at start AND at exit.
param(
    [string]$Drawing,
    [string]$AcadExe,
    [switch]$KeepExistingAcad,
    [int]$StartupWaitSec = 40
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$work = Join-Path $env:TEMP 'hydrocomplete-gui-smoke'
$bundleDll = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\HydroComplete.bundle\Contents\HydroComplete.Civil3D.dll'

if (-not $AcadExe) {
    $AcadExe = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe'
}
if (-not (Test-Path $AcadExe)) {
    Write-Host "SKIP: Civil 3D 2026 not found at $AcadExe"
    exit 2
}
if (-not $Drawing) {
    $Drawing = 'C:\Program Files\Autodesk\AutoCAD 2026\C3D\Help\Civil Tutorials\Drawings\Pipe Networks-3.dwg'
}
if (-not (Test-Path $Drawing)) {
    Write-Host "SKIP: seed drawing not found: $Drawing"
    exit 2
}

if (-not $KeepExistingAcad) {
    Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$localDwg = Join-Path $work 'gui-smoke.dwg'
Copy-Item $Drawing $localDwg -Force
Get-ChildItem $work -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force
# A force-killed prior session leaves stale .dwl/.dwl2 locks that make the
# next open prompt "open read-only?" - a modal question COM cannot answer.
Get-ChildItem $work -Filter 'gui-smoke.dwl*' -ErrorAction SilentlyContinue | Remove-Item -Force

# 4x4 white PNG for HC_BACKGROUND.
$pngPath = Join-Path $work 'hc-bg-test.png'
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 4, 4
for ($x = 0; $x -lt 4; $x++) { for ($y = 0; $y -lt 4; $y++) { $bmp.SetPixel($x, $y, [System.Drawing.Color]::White) } }
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Start-Process -FilePath $AcadExe -ArgumentList '/product', 'C3D', '/nologo' -WindowStyle Normal | Out-Null
Write-Host '[1/5] Launched Civil 3D'

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

while ($acad.Documents.Count -gt 0) {
    try { $acad.Documents.Item(0).Close($false) } catch { break }
    Start-Sleep -Milliseconds 500
}
$opened = $false
for ($try = 0; $try -lt 40; $try++) {
    try { $null = $acad.Documents.Open($localDwg); $opened = $true; break } catch { Start-Sleep -Seconds 2 }
}
if (-not $opened) { throw "Documents.Open failed for $localDwg" }
Start-Sleep -Seconds 5
Write-Host "[2/5] COM v$($acad.Version) - $($acad.ActiveDocument.Name)"

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 6) {
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

# Separate-process watchdog: waits for a top-level window whose name contains
# $TitlePart, then UIA-invokes the button named $ButtonName. On timeout it
# tries the Cancel button so the modal never wedges the session.
function Start-DialogWatchdog([string]$TitlePart, [string]$ButtonName, [int]$TimeoutSec = 90) {
    Start-Job -ArgumentList $TitlePart, $ButtonName, $TimeoutSec -ScriptBlock {
        param($TitlePart, $ButtonName, $TimeoutSec)
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windowCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)

        function Invoke-ButtonIn($window, $name) {
            $btnCond = New-Object System.Windows.Automation.AndCondition @(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Button)),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $name))
            )
            $btn = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            if ($btn) {
                $pattern = $btn.GetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern)
                $pattern.Invoke()
                return $true
            }
            return $false
        }

        # The HydroComplete WPF dialogs are DESCENDANTS of the Civil 3D main
        # window in the UIA tree, not top-level desktop windows - search the
        # descendants of each Civil 3D frame, not RootElement's children.
        function Find-Dialog($part) {
            $tops = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCond)
            foreach ($top in $tops) {
                $topName = ''
                try { $topName = [string]$top.Current.Name } catch { continue }
                if ($topName -notlike '*Civil 3D*') { continue }
                $subs = $top.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowCond)
                foreach ($w in $subs) {
                    $name = ''
                    try { $name = [string]$w.Current.Name } catch { continue }
                    if ($name -like "*$part*") { return $w }
                }
            }
            return $null
        }

        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 1000
            $dlg = Find-Dialog $TitlePart
            if ($dlg) {
                if (Invoke-ButtonIn $dlg $ButtonName) {
                    "INVOKED '$ButtonName' on '$($dlg.Current.Name)'"
                    return
                }
            }
        }
        # Timeout: try to cancel so the modal never hangs the session.
        $dlg = Find-Dialog $TitlePart
        if ($dlg -and (Invoke-ButtonIn $dlg 'Cancel')) {
            "TIMEOUT - cancelled '$($dlg.Current.Name)'"
            return
        }
        "TIMEOUT - dialog '$TitlePart' never seen"
    }
}

function Invoke-DialogCommand(
    [string]$Label, [string]$Cmd, [string]$TitlePart, [string]$ButtonName, [int]$WaitSec = 8) {
    $watchdog = Start-DialogWatchdog -TitlePart $TitlePart -ButtonName $ButtonName
    try {
        Send-Cmd $Label $Cmd $WaitSec
    }
    finally {
        $result = ''
        if (Wait-Job $watchdog -Timeout 100) {
            $result = (Receive-Job $watchdog) -join '; '
        }
        Remove-Job $watchdog -Force -ErrorAction SilentlyContinue
        Write-Host "    watchdog: $result"
    }
}

try {
    $dll = $bundleDll
    if (-not (Test-Path $dll)) {
        $dll = Join-Path $repoRoot 'src\HydroComplete.Civil3D\bin\Release\net8.0-windows\HydroComplete.Civil3D.dll'
    }
    Send-Cmd 'SECURELOAD' "_.SECURELOAD`n0`n" 3
    Send-Cmd 'NETLOAD' "NETLOAD`n`"$dll`"`n" 12
    Send-Cmd 'LOGFILE ON' "_.LOGFILEPATH`n`"$work\`"`n_.LOGFILEMODE`n1`n" 3

    Write-Host '[3/5] Driving GUI commands'
    # All Editor answers are pre-queued in the SendCommand string; dialogs are
    # dismissed by the UIA watchdog. Each command isolated so one failure
    # cannot sink the rest.

    # HC_INLETS: dialog 'Run check', then pre-queued Enter answers the Q prompt.
    try {
        Invoke-DialogCommand 'HC_INLETS' "HC_INLETS`n`n" 'HEC-22 Inlet' 'Run check' 10
    } catch { Write-Host "  WARN: $_" }

    # HC_PROFILE: dialog 'Run', then pre-queued 0,0 answers the insertion point.
    try {
        Invoke-DialogCommand 'HC_PROFILE' "HC_PROFILE`n0,0`n" 'Chainage Profile' 'Run' 12
    } catch { Write-Host "  WARN: $_" }

    # HC_NETWORK_EDIT: grid window; watchdog cancels without saving.
    try {
        Invoke-DialogCommand 'HC_NETWORK_EDIT' "HC_NETWORK_EDIT`n" 'Network Editor' 'Cancel' 8
    } catch { Write-Host "  WARN: $_" }

    # HC_BACKGROUND: no dialog - path, insertion point, width all pre-queued.
    try {
        Send-Cmd 'HC_BACKGROUND' "HC_BACKGROUND`n$pngPath`n0,0`n`n" 8
    } catch { Write-Host "  WARN: $_" }

    # HC_DAG LAST: nothing is sent to the session after the palette opens.
    try {
        Send-Cmd 'HC_DAG (palette opens)' "HC_DAG`n" 20
    } catch { Write-Host "  WARN: $_" }
}
finally {
    Write-Host '[4/5] Checks (log under LOGFILEPATH)'
    Start-Sleep -Seconds 3
    $log = ''
    $newest = Get-ChildItem $work -Filter '*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest) {
        # AutoCAD holds the log open; read shared.
        $fs = [System.IO.File]::Open($newest.FullName, 'Open', 'Read', 'ReadWrite')
        try {
            $sr = New-Object System.IO.StreamReader($fs)
            $log = $sr.ReadToEnd()
        } finally { $fs.Dispose() }
    }

    if (-not $KeepExistingAcad) {
        Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    if (-not $log) {
        Write-Host 'SMOKE FAIL: no command-line log captured under LOGFILEPATH.'
        exit 1
    }

    $checks = [ordered]@{
        'HC_INLETS completed inlet check'   = ($log -match 'inlet check \(\d+ location\(s\)\)')
        'HC_PROFILE drew profile polylines' = ($log -match 'Drew \d+ profile polyline\(s\)')
        'HC_NETWORK_EDIT cancelled cleanly' = ($log -match 'Network editor cancelled|network overrides saved')
        'HC_BACKGROUND attached image'      = ($log -match 'HydroComplete: background image')
        'HC_DAG palette opened'             = ($log -match 'HydroComplete Model Builder opened')
    }

    $failed = @()
    foreach ($kv in $checks.GetEnumerator()) {
        $status = if ($kv.Value) { 'PASS' } else { 'FAIL' }
        Write-Host "  $($kv.Key): $status"
        if (-not $kv.Value) { $failed += $kv.Key }
    }

    if ($failed.Count -eq 0) {
        Write-Host '[5/5] SMOKE OK: GUI commands verified via desktop session.'
        exit 0
    }
    Write-Host "[5/5] SMOKE FAIL: $($failed -join '; ')"
    Write-Host "Log: $($newest.FullName)"
    exit 1
}
