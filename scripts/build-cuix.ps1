# Generate dist/HydroComplete.bundle/Contents/HydroComplete.cuix (partial CUIX with the
# HydroComplete ribbon tab) from scripts/ribbon.json, out-of-process, through AcCui.dll.
#
# Usage (Windows PowerShell 5.1, NOT pwsh):
#   python scripts/build-icons.py
#   powershell -ExecutionPolicy Bypass -File scripts/build-cuix.ps1
#
# Ported from the Survey & Parcel Field Kit generator (Desktop/AI/C3D-AutoCAD), whose
# CUIX passed Autodesk Marketplace review. Same rules, all learned the hard way:
#  - AcCui.dll must be the AutoCAD 2023 copy (.NET Framework; 2025+ is .NET 8 and will
#    not load in PowerShell 5.1). Newer AutoCAD migrates the schema forward on load.
#  - Ribbon objects take their parent in the ctor but do NOT register themselves:
#    every panel/row/button/tab needs an explicit .Add() or it is silently dropped.
#  - Images cannot be set out-of-process (NullReferenceException) and cannot be
#    resolved from a support path: they are patched into the saved package, embedded
#    at the ZIP ROOT, with png declared in [Content_Types].xml.
#  - Panels and tab get STABLE ElementIDs. AutoCAD merges the tab into the workspace
#    by ID; random IDs orphan the merge and the ribbon silently disappears on upgrade.
#  - At most 3 small buttons per row; a wider row draws as one truncated button.
#
# This replaces the old programmatic ribbon (RibbonBuilder.cs), whose buttons had no
# images - Autodesk review 2026-11-14: "required ribbon icons are not provided".

$ErrorActionPreference = 'Stop'

$AcadDir   = 'C:\Program Files\Autodesk\AutoCAD 2023'
$Root      = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Spec      = Get-Content -Raw -Encoding UTF8 (Join-Path $Root 'scripts\ribbon.json') | ConvertFrom-Json
$IconDir   = Join-Path $Root 'packaging\icons'
$OutDir    = Join-Path $Root 'dist\HydroComplete.bundle\Contents'
$OutFile   = Join-Path $OutDir 'HydroComplete.cuix'
$GroupName = 'HYDROCOMPLETE'
$MaxRows   = 3

Add-Type -Path (Join-Path $AcadDir 'AcCui.dll')
New-Item -ItemType Directory -Force $OutDir | Out-Null

$allButtons = @($Spec | ForEach-Object { $_.buttons })
$nCmd = $allButtons.Count

function Split-IntoRows($items, $maxRows) {
    $items = @($items)
    $n = $items.Count
    $rows = [Math]::Min($n, $maxRows)
    $base = [Math]::Floor($n / $rows)
    $rem  = $n % $rows
    $out = @(); $i = 0
    for ($r = 0; $r -lt $rows; $r++) {
        $take = $base + $(if ($r -lt $rem) { 1 } else { 0 })
        $out += ,@($items[$i..($i + $take - 1)])
        $i += $take
    }
    return $out
}

function Get-IconStem([string]$cmd) { 'hc_' + $cmd.Substring(3).ToLower() }

if (Test-Path $OutFile) { Remove-Item $OutFile -Force }

$cs = New-Object Autodesk.AutoCAD.Customization.CustomizationSection
$cs.MenuGroupName = $GroupName
$cs.MenuGroupDisplayName = 'HydroComplete'
$cs.SaveAs($OutFile) | Out-Null

$cs = New-Object Autodesk.AutoCAD.Customization.CustomizationSection($OutFile)
$mg = $cs.MenuGroup
if (-not $mg) { throw 'MenuGroup null after seed+reopen.' }

# --- Macros ---
$macroGroup = New-Object Autodesk.AutoCAD.Customization.MacroGroup('HydroComplete', $mg)
$macros = @{}
foreach ($b in $allButtons) {
    $m = New-Object Autodesk.AutoCAD.Customization.MenuMacro(
        $macroGroup, $b.label, "^C^C$($b.cmd)", "HCM_$($b.cmd)")
    $m.macro.HelpString = $b.tip
    $macros[$b.cmd] = $m
}

# --- Panels ---
$ribbonRoot = $mg.RibbonRoot
$panelIds = @()
foreach ($p in $Spec) {
    $key = ($p.name.ToUpper() -replace '[^A-Z0-9]', '')
    $panel = New-Object Autodesk.AutoCAD.Customization.RibbonPanelSource($ribbonRoot)
    $panel.Name = "HydroComplete - $($p.name)"
    $panel.Text = $p.name
    $panel.ElementID = "ID_HC_PANEL_$key"
    $ribbonRoot.RibbonPanelSources.Add($panel) | Out-Null

    foreach ($rowBtns in (Split-IntoRows $p.buttons $MaxRows)) {
        $row = New-Object Autodesk.AutoCAD.Customization.RibbonRow
        $panel.Items.Add($row) | Out-Null
        foreach ($b in $rowBtns) {
            $btn = New-Object Autodesk.AutoCAD.Customization.RibbonCommandButton($row)
            $btn.MacroID      = $macros[$b.cmd].ElementID
            $btn.Text         = $b.label
            $btn.ButtonStyle  = [Autodesk.AutoCAD.Customization.RibbonButtonStyle]::SmallWithText
            $btn.TooltipTitle = "$($b.label)  ($($b.cmd))"
            $row.Items.Add($btn) | Out-Null
        }
    }
    $brk = New-Object Autodesk.AutoCAD.Customization.RibbonPanelBreak($panel)
    $panel.Items.Add($brk) | Out-Null
    $panelIds += $panel.ElementID
}

# --- Tab ---
$tab = New-Object Autodesk.AutoCAD.Customization.RibbonTabSource($ribbonRoot)
$tab.Name = 'HydroComplete'
$tab.Text = 'HydroComplete'
$tab.KeyTip = 'HC'
$tab.ElementID = 'ID_TAB_HYDROCOMPLETE'
$tab.DefaultDisplay = [Autodesk.AutoCAD.Customization.DefaultDisplay]::AddToWorkspace
$tab.WorkspaceBehavior = [Autodesk.AutoCAD.Customization.TabWorkspaceBehavior]::MergeOrAddTab
$tab.Aliases.Add('ID_TAB_HYDROCOMPLETE') | Out-Null
$ribbonRoot.RibbonTabSources.Add($tab) | Out-Null
foreach ($panelId in $panelIds) {
    $ref = New-Object Autodesk.AutoCAD.Customization.RibbonPanelSourceReference($tab)
    $ref.PanelId = $panelId
    $tab.Items.Add($ref) | Out-Null
}
$cs.Save() | Out-Null

# --- Embed icons ---
Add-Type -AssemblyName System.IO.Compression.FileSystem
$work = Join-Path $OutDir '_cuix_icon_work'
if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
[System.IO.Compression.ZipFile]::ExtractToDirectory($OutFile, $work)
$embedded = 0
foreach ($b in $allButtons) {
    foreach ($px in 16, 32) {
        $f = Join-Path $IconDir ("{0}_{1}.png" -f (Get-IconStem $b.cmd), $px)
        if (-not (Test-Path $f)) { throw "Missing icon $f - run scripts/build-icons.py" }
        Copy-Item $f $work -Force
        $embedded++
    }
}
$ctPath = Join-Path $work '[Content_Types].xml'
[xml]$ct = Get-Content -LiteralPath $ctPath -Raw
if (-not @($ct.Types.ChildNodes | Where-Object { $_.Extension -eq 'png' }).Count) {
    $node = $ct.CreateElement('Default', 'http://schemas.openxmlformats.org/package/2006/content-types')
    $node.SetAttribute('Extension', 'png'); $node.SetAttribute('ContentType', 'image/png')
    $ct.Types.AppendChild($node) | Out-Null
    $ct.Save($ctPath)
}
$menuPath = Join-Path $work 'MenuGroup.cui'
[xml]$doc = Get-Content -LiteralPath $menuPath -Raw
$patched = 0
foreach ($mm in $doc.GetElementsByTagName('MenuMacro')) {
    $uid = $mm.GetAttribute('UID')
    if ($uid -notlike 'HCM_HC_*') { continue }
    $stem = Get-IconStem ($uid -replace '^HCM_', '')
    foreach ($pair in @(@('SmallImage', 16), @('LargeImage', 32))) {
        $node = $mm.GetElementsByTagName($pair[0]) | Select-Object -First 1
        if ($node) { $node.SetAttribute('Name', ("{0}_{1}.png" -f $stem, $pair[1])); $patched++ }
    }
}
$doc.Save($menuPath)
[System.IO.File]::Delete($OutFile)
[System.IO.Compression.ZipFile]::CreateFromDirectory($work, $OutFile)
Remove-Item -LiteralPath $work -Recurse -Force

# --- Verify by reopening ---
$check = New-Object Autodesk.AutoCAD.Customization.CustomizationSection($OutFile)
$cmg = $check.MenuGroup
$rowType = [Autodesk.AutoCAD.Customization.RibbonRow]
$nMacro = ($cmg.MacroGroups | ForEach-Object { $_.MenuMacros.Count } | Measure-Object -Sum).Sum
$nPanel = $cmg.RibbonRoot.RibbonPanelSources.Count
$nTab   = $cmg.RibbonRoot.RibbonTabSources.Count
$nRef   = ($cmg.RibbonRoot.RibbonTabSources | ForEach-Object { $_.Items.Count } | Measure-Object -Sum).Sum
$rows   = $cmg.RibbonRoot.RibbonPanelSources | ForEach-Object { $_.Items | Where-Object { $rowType.IsInstanceOfType($_) } }
$nBtn   = ($rows | ForEach-Object { $_.Items.Count } | Measure-Object -Sum).Sum
$maxPerRow = ($rows | ForEach-Object { $_.Items.Count } | Measure-Object -Maximum).Maximum

$zip = [System.IO.Compression.ZipFile]::OpenRead($OutFile)
try {
    $parts = @($zip.Entries | ForEach-Object { $_.FullName })
    $r = New-Object System.IO.StreamReader(($zip.Entries | Where-Object { $_.Name -eq 'MenuGroup.cui' }).Open())
    $menuXml = $r.ReadToEnd(); $r.Close()
} finally { $zip.Dispose() }
$named = [regex]::Matches($menuXml, '<(?:Small|Large)Image Name="([^"]+)"')
$dangling = @($named | ForEach-Object { $_.Groups[1].Value } | Where-Object { $parts -notcontains $_ })

Write-Host "Wrote $OutFile ($((Get-Item $OutFile).Length) bytes)"
Write-Host "  macros $nMacro, buttons $nBtn (expect $nCmd); panels $nPanel, refs $nRef (expect $($Spec.Count)); tab $nTab; max/row $maxPerRow"
Write-Host "  icons: $embedded embedded, $($named.Count) refs, $($dangling.Count) dangling"
if ($nMacro -ne $nCmd -or $nBtn -ne $nCmd -or $nPanel -ne $Spec.Count -or $nRef -ne $Spec.Count -or $nTab -ne 1) { throw 'Verification FAILED: counts' }
if ($maxPerRow -gt 3) { throw "Verification FAILED: $maxPerRow buttons in a row" }
if ($named.Count -ne 2 * $nCmd -or $dangling.Count) { throw 'Verification FAILED: icon references' }
$unstable = @($cmg.RibbonRoot.RibbonPanelSources | Where-Object { $_.ElementID -notlike 'ID_HC_PANEL_*' })
if ($unstable.Count -or $cmg.RibbonRoot.RibbonTabSources[0].ElementID -ne 'ID_TAB_HYDROCOMPLETE') { throw 'Verification FAILED: unstable IDs' }
Write-Host 'Verification passed.'
