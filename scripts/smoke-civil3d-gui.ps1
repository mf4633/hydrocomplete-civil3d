# Desktop GUI smoke: HC_INLETS, HC_PROFILE, HC_NETWORK_EDIT, HC_BACKGROUND, HC_DAG.
# These need the full Civil 3D app: modal WPF dialogs, GetPoint picks, WebView2 palette.
# Dialogs are driven by focused keystrokes (Enter = each dialog's IsDefault button);
# command-line output is captured via LOGFILEMODE/LOGFILEPATH and asserted per command.
#
# REQUIRES AN IDLE DESKTOP: SendKeys goes to the focused window. If anyone is
# typing or clicking while this runs, keystrokes miss the Civil 3D dialogs (the
# run stalls) and can leak into other applications. Run it unattended.
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

$wsh = New-Object -ComObject WScript.Shell

function Send-Keys([string]$keys, [int]$waitSec = 3) {
    $acadPid = (Get-Process acad | Select-Object -First 1).Id
    $wsh.AppActivate($acadPid) | Out-Null
    Start-Sleep -Milliseconds 800
    $wsh.SendKeys($keys)
    Start-Sleep -Seconds $waitSec
}

function Send-Cmd([string]$label, [string]$cmd, [int]$waitSec = 6) {
    for ($try = 0; $try -lt 40; $try++) {
        try {
            $script:acad.ActiveDocument.SendCommand($cmd)
            Write-Host "  $label"
            Start-Sleep -Seconds $waitSec
            return
        } catch {
            # A stray modal dialog (e.g. a repeated command) rejects COM calls;
            # ESC it away every few retries so the run self-heals.
            if ($try % 4 -eq 3) { Send-Keys '{ESC}' 1 }
            Start-Sleep -Milliseconds 1500
        }
    }
    throw "SendCommand failed: $label"
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
    # Each command is isolated: a failure ESCes back to quiescent and moves on.
    # The trailing ESC after each dialog sequence dismisses any dialog that a
    # stray Enter re-opened via repeat-last-command (ESC at a quiescent prompt
    # is harmless).

    # HC_INLETS: modal dialog (Enter = Run check), then one Q prompt (Enter = 1 cfs).
    try {
        Send-Cmd 'HC_INLETS (dialog opens)' "HC_INLETS`n" 8
        Send-Keys '{ENTER}' 6
        Send-Keys '{ENTER}' 6
        Send-Keys '{ESC}' 2
    } catch { Write-Host "  WARN: $_"; Send-Keys '{ESC}{ESC}' 3 }

    # HC_PROFILE: modal dialog (Enter = Run), then insertion point typed at command line.
    try {
        Send-Cmd 'HC_PROFILE (dialog opens)' "HC_PROFILE`n" 8
        Send-Keys '{ENTER}' 6
        Send-Keys '0,0{ENTER}' 8
        Send-Keys '{ESC}' 2
    } catch { Write-Host "  WARN: $_"; Send-Keys '{ESC}{ESC}' 3 }

    # HC_NETWORK_EDIT: grid window; ESC cancels without saving.
    try {
        Send-Cmd 'HC_NETWORK_EDIT (window opens)' "HC_NETWORK_EDIT`n" 8
        Send-Keys '{ESC}' 5
    } catch { Write-Host "  WARN: $_"; Send-Keys '{ESC}{ESC}' 3 }

    # HC_BACKGROUND: no dialog - path, insertion point, width all via queued input.
    try {
        Send-Cmd 'HC_BACKGROUND' "HC_BACKGROUND`n$pngPath`n0,0`n`n" 8
        Send-Keys '{ESC}' 2
    } catch { Write-Host "  WARN: $_"; Send-Keys '{ESC}{ESC}' 3 }

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
