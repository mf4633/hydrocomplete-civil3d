# Pre-submission validation for Autodesk App Store bundle packaging.
# Exits non-zero on any failure.
param(
    [string]$Version,
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$bundle = Join-Path $root 'dist\HydroComplete.bundle'
$manifest = Join-Path $bundle 'PackageContents.xml'
$contents = Join-Path $bundle 'Contents'
$icon = Join-Path $contents 'PackageIcon.png'
$csproj = Join-Path $root 'src\HydroComplete.Civil3D\HydroComplete.Civil3D.csproj'
$commandsDir = Join-Path $root 'src\HydroComplete.Civil3D\Commands'
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
    Write-Host "FAIL: $Message" -ForegroundColor Red
}

function Add-Pass {
    param([string]$Message)
    Write-Host "OK:   $Message" -ForegroundColor Green
}

function Get-ProjectVersion {
    [xml]$xml = Get-Content $csproj
    $versionNode = $xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    if (-not $versionNode -or -not $versionNode.Version) {
        throw "Could not read <Version> from $csproj"
    }
    return [string]$versionNode.Version
}

function Get-SourceCommands {
    $pattern = '\[CommandMethod\("([^"]+)"(?:,\s*[^)]+)?\)\]'
    $found = Get-ChildItem $commandsDir -Filter '*.cs' -Recurse |
        ForEach-Object {
            [regex]::Matches((Get-Content $_.FullName -Raw), $pattern) |
                ForEach-Object { $_.Groups[1].Value }
        } |
        Where-Object { $_ -like 'HC_*' } |
        Sort-Object -Unique
    return ,$found
}

function Get-ManifestCommands {
    param([string]$ManifestText)
    $pattern = 'Local="(HC_[^"]+)"'
    [regex]::Matches($ManifestText, $pattern) |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
}

Write-Host "HydroComplete App Store preflight"
Write-Host "Root: $root"
Write-Host ""

if (-not $Version) {
    $Version = Get-ProjectVersion
}
Write-Host "Expected version: $Version"
Write-Host ""

# --- Bundle layout ---
if (-not (Test-Path $manifest)) {
    Add-Failure "PackageContents.xml not found at $manifest"
}
else {
    Add-Pass "PackageContents.xml present"
}

$requiredDlls = @(
    (Join-Path $contents 'HydroComplete.Civil3D.dll'),
    (Join-Path $contents 'HydroComplete.Engine.dll')
)
foreach ($dll in $requiredDlls) {
    if (-not (Test-Path $dll)) {
        Add-Failure "Missing bundle DLL: $dll"
    }
    else {
        Add-Pass "Bundle DLL present: $(Split-Path $dll -Leaf)"
    }
}

if (-not (Test-Path $icon)) {
    Add-Failure "PackageIcon.png missing at $icon (96x96 PNG required)"
}
else {
    Add-Pass "PackageIcon.png present"
}

# --- Version string sync ---
$versionFiles = @(
    @{ Path = $csproj; Pattern = "<Version>$([regex]::Escape($Version))</Version>"; Label = 'csproj <Version>' },
    @{ Path = $manifest; Pattern = "AppVersion=`"$([regex]::Escape($Version))`""; Label = 'manifest AppVersion' },
    @{ Path = (Join-Path $root 'src\HydroComplete.Civil3D\Plugin.cs'); Pattern = "for Civil 3D $([regex]::Escape($Version)) loaded"; Label = 'Plugin.cs banner' },
    @{ Path = (Join-Path $root 'verify-install.ps1'); Pattern = "Startup banner: HydroComplete for Civil 3D $([regex]::Escape($Version)) loaded"; Label = 'verify-install.ps1' },
    @{ Path = (Join-Path $root 'dist\app-store\LISTING.md'); Pattern = [regex]::Escape("Version at submission:** $Version"); Label = 'LISTING.md version line' }
)

foreach ($vf in $versionFiles) {
    if (-not (Test-Path $vf.Path)) {
        Add-Failure "$($vf.Label) file missing: $($vf.Path)"
        continue
    }
    $text = Get-Content $vf.Path -Raw
    if ($text -notmatch $vf.Pattern) {
        Add-Failure "$($vf.Label) does not contain version $Version"
    }
    else {
        Add-Pass "$($vf.Label) matches $Version"
    }
}

# --- Command registration ---
if (Test-Path $manifest) {
    $manifestText = Get-Content $manifest -Raw
    $sourceCommands = Get-SourceCommands
    $manifestCommands = Get-ManifestCommands -ManifestText $manifestText

    Write-Host ""
    Write-Host "Commands in source: $($sourceCommands.Count)"
    Write-Host "Commands in manifest (unique): $($manifestCommands.Count)"

    foreach ($cmd in $sourceCommands) {
        if ($manifestCommands -notcontains $cmd) {
            Add-Failure "Source command missing from PackageContents.xml: $cmd"
        }
    }

    foreach ($cmd in $manifestCommands) {
        if ($sourceCommands -notcontains $cmd) {
            Add-Failure "Manifest command not found in source: $cmd"
        }
    }

    if ($sourceCommands.Count -eq $manifestCommands.Count) {
        $manifestOk = $true
        foreach ($cmd in $sourceCommands) {
            if ($manifestCommands -notcontains $cmd) { $manifestOk = $false; break }
        }
        if ($manifestOk) {
            Add-Pass "All $($sourceCommands.Count) HC_* commands registered in manifest"
        }
    }

    # Command classes must be registered in Plugin.cs for auto-load discovery
    $pluginPath = Join-Path $root 'src\HydroComplete.Civil3D\Plugin.cs'
    $pluginText = Get-Content $pluginPath -Raw
    $commandFiles = Get-ChildItem $commandsDir -Filter '*Commands.cs'
    $missingPluginClasses = @()
    foreach ($file in $commandFiles) {
        $className = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $expected = "Commands.$className"
        if ($pluginText -notmatch [regex]::Escape($expected)) {
            $missingPluginClasses += $className
            Add-Failure "Plugin.cs missing CommandClass registration for $className"
        }
    }
    if ($missingPluginClasses.Count -eq 0) {
        Add-Pass "All $($commandFiles.Count) command classes registered in Plugin.cs"
    }

    $expectedSeries = @('R25.0', 'R25.1')
    foreach ($series in $expectedSeries) {
        if ($manifestText -notmatch "SeriesMin=`"$series`"") {
            Add-Failure "PackageContents.xml missing ComponentEntry for $series"
        }
        else {
            Add-Pass "RuntimeRequirements for $series present"
        }
    }

    # R24.3 net48 - required only when net48 DLLs are staged for Civil 3D 2024
    $net48Dll = Join-Path $contents 'net48\HydroComplete.Civil3D.dll'
    if (Test-Path $net48Dll) {
        if ($manifestText -notmatch 'SeriesMin="R24\.3"') {
            Add-Failure 'Contents/net48 present but PackageContents.xml has no R24.3 ComponentEntry'
        }
        else {
            Add-Pass 'R24.3 net48 ComponentEntry present (Civil 3D 2024)'
        }
    }
    else {
        Write-Host "INFO: No Contents/net48 build - R24.3 Civil 3D 2024 not included (expected until net48 is built)"
    }
}

# --- Zip structure (optional) ---
if (-not $ZipPath) {
    $ZipPath = Join-Path $root "dist\HydroComplete-$Version.zip"
}

if (Test-Path $ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = $zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
        $requiredZip = @(
            'PackageContents.xml',
            'Contents/HydroComplete.Civil3D.dll',
            'Contents/HydroComplete.Engine.dll',
            'Contents/PackageIcon.png'
        )
        foreach ($entry in $requiredZip) {
            if ($entries -notcontains $entry) {
                Add-Failure "Zip missing entry: $entry ($ZipPath)"
            }
            else {
                Add-Pass "Zip contains $entry"
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}
else {
    Write-Host "INFO: Release zip not found at $ZipPath - run .\scripts\release.ps1 after preflight fixes"
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Preflight FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "Preflight PASSED - bundle ready for App Store packaging." -ForegroundColor Green
exit 0