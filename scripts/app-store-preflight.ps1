# Pre-submission validation for Autodesk App Store bundle packaging.
# Exits non-zero on any failure.
param(
    [string]$Version,
    [string]$ZipPath,
    # Treat an unsigned/invalid bundle as a hard failure. Off by default so the
    # pre-certificate workflow still passes; auto-enabled once a signing cert is
    # configured (HC_SIGN_CERT_THUMBPRINT). Pass -RequireSigning for the final
    # submission build.
    [switch]$RequireSigning
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

# --- Code signing (Authenticode) ---
# Autodesk App Store expects signed binaries; unsigned assemblies trip SmartScreen
# and review friction. Signing happens at release time (scripts/sign-release.ps1)
# once an OV/EV certificate exists. This check WARNS by default so the pre-cert
# workflow still passes, and becomes a hard failure when -RequireSigning is passed
# or HC_SIGN_CERT_THUMBPRINT is set (i.e. you have a cert and intend to ship signed).
$requireSigning = $RequireSigning.IsPresent -or [bool]$env:HC_SIGN_CERT_THUMBPRINT
$signTargets = @()
foreach ($name in @('HydroComplete.Civil3D.dll', 'HydroComplete.Engine.dll')) {
    $net8 = Join-Path $contents $name
    $net48 = Join-Path $contents "net48\$name"
    if (Test-Path $net8) { $signTargets += $net8 }
    if (Test-Path $net48) { $signTargets += $net48 }
}
if ($signTargets.Count -eq 0) {
    Write-Host "INFO: No bundle DLLs staged yet - signing check skipped (build first)."
}
elseif (-not (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
    Write-Host "INFO: Get-AuthenticodeSignature unavailable (non-Windows host) - signing check skipped."
}
else {
    foreach ($dll in $signTargets) {
        $rel = $dll.Substring($root.Length).TrimStart('\')
        $sig = Get-AuthenticodeSignature $dll
        if ($sig.Status -eq 'Valid') {
            $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { 'unknown signer' }
            Add-Pass "Signed: $rel ($signer)"
        }
        elseif ($requireSigning) {
            Add-Failure "Unsigned or invalid signature (status: $($sig.Status)): $rel"
        }
        else {
            Write-Host "WARN: $rel is not validly signed (status: $($sig.Status)). Sign before App Store submission (scripts/sign-release.ps1)." -ForegroundColor Yellow
        }
    }
    if ($requireSigning) {
        Add-Pass "Signing enforced (RequireSigning / HC_SIGN_CERT_THUMBPRINT)"
    }
    else {
        Write-Host "INFO: Signing not enforced. Run with -RequireSigning (or set HC_SIGN_CERT_THUMBPRINT) for the final submission build." -ForegroundColor Yellow
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